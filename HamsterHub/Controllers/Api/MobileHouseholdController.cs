using System.Data;
using HamsterHub.Contracts;
using HamsterHub.Data;
using HamsterHub.Models;
using HamsterHub.Security;
using HamsterHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Controllers.Api;

[ApiController, Route("api/v1/memberships/{memberId:int}"), Authorize(Policy = MobileAuthentication.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MobileHouseholdController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    PointBalanceService points,
    RewardService rewards,
    UploadedImageService images,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("household")]
    public async Task<ActionResult<HouseholdHubDto>> Household(int memberId, CancellationToken cancellationToken)
    {
        var viewer = await GetMemberAsync(memberId, cancellationToken);
        if (viewer is null) return Forbid();
        var isParent = viewer.MemberRole == HouseholdMemberRole.Parent;

        var members = await db.HouseholdMembers.AsNoTracking().Include(item => item.User)
            .Where(item => item.HouseholdId == viewer.HouseholdId && item.IsActive && item.User.IsActive)
            .OrderBy(item => item.User.DisplayName).ToListAsync(cancellationToken);
        var memberItems = new List<HouseholdMemberItemDto>();
        foreach (var item in members)
        {
            if (!isParent && item.MemberRole == HouseholdMemberRole.Child && item.Id != viewer.Id &&
                (!viewer.CanViewOtherChildrenHistory || !item.ShareHistoryWithChildren)) continue;
            memberItems.Add(new HouseholdMemberItemDto(item.Id, item.User.DisplayName,
                isParent ? item.User.Email ?? "" : "", item.MemberRole.ToString(), item.Id == viewer.Id,
                item.User.ProfilePhotoPath,
                await points.GetBalanceAsync(viewer.HouseholdId, item.UserId, cancellationToken)));
        }

        var pets = await db.Pets.AsNoTracking().Where(item => item.HouseholdId == viewer.HouseholdId && item.IsActive)
            .OrderBy(item => item.Name).Select(item => new PetItemDto(item.Id, item.Name, item.Species,
                item.BirthDate, item.PhotoPath)).ToListAsync(cancellationToken);
        var categories = await db.CareCategories.AsNoTracking()
            .Where(item => item.IsActive && (item.HouseholdId == null || item.HouseholdId == viewer.HouseholdId))
            .ToListAsync(cancellationToken);
        var categoryItems = categories.OrderBy(CategoryName).Select(item =>
            new CategoryItemDto(item.Id, CategoryName(item))).ToList();

        var taskQuery = db.CareTasks.AsNoTracking().Include(item => item.Pet)
            .Include(item => item.CareCategory).Include(item => item.AssignedMember).ThenInclude(item => item.User)
            .Where(item => item.HouseholdId == viewer.HouseholdId && item.IsActive && (item.PetId == null || item.Pet!.IsActive));
        if (!isParent) taskQuery = taskQuery.Where(item => item.AssignedMemberId == viewer.Id);
        var tasks = await taskQuery.OrderBy(item => item.Pet == null ? "" : item.Pet.Name).ToListAsync(cancellationToken);
        var taskItems = tasks.Select(item => new ManagedTaskItemDto(item.Id, item.PetId, item.Pet?.Name ?? CategoryName(item.CareCategory),
            item.CareCategoryId, item.Name ?? CategoryName(item.CareCategory), item.AssignedMemberId,
            item.AssignedMember.User.DisplayName, item.Frequency.ToString(), item.PointValue,
            TaskImage(item), false, TaskReminderSettings.Read(item))).ToList();

        var rewardQuery = db.Rewards.AsNoTracking().Include(item => item.VisibleToMembers)
            .Where(item => item.HouseholdId == viewer.HouseholdId && item.IsActive);
        if (!isParent) rewardQuery = rewardQuery.Where(item =>
            item.VisibleToMembers.Any(visibility => visibility.HouseholdMemberId == viewer.Id));
        var rewardRows = await rewardQuery.OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var pendingIds = await db.RewardRedemptions.AsNoTracking().Where(item =>
                item.HouseholdId == viewer.HouseholdId && item.HouseholdMemberId == viewer.Id &&
                item.Status == RewardRedemptionStatus.Pending)
            .Select(item => item.RewardId).ToListAsync(cancellationToken);
        var balance = await points.GetBalanceAsync(viewer.HouseholdId, viewer.UserId, cancellationToken);
        var rewardItems = rewardRows.Select(item => new RewardItemDto(item.Id, item.Name, item.PointCost,
            item.ImagePath, item.VisibleToMembers.Select(link => link.HouseholdMemberId).ToList(),
            balance >= item.PointCost, pendingIds.Contains(item.Id))).ToList();

        IReadOnlyList<RewardRequestItemDto> requests = [];
        if (isParent)
        {
            var requestRows = await db.RewardRedemptions.AsNoTracking()
                .Include(item => item.HouseholdMember).ThenInclude(item => item.User)
                .Where(item => item.HouseholdId == viewer.HouseholdId &&
                    item.Status == RewardRedemptionStatus.Pending).ToListAsync(cancellationToken);
            requests = requestRows.OrderByDescending(item => item.RequestedAt)
                .Select(item => new RewardRequestItemDto(item.Id, item.RewardName,
                    item.HouseholdMember.User.DisplayName, item.PointsCost, item.RequestedAt,
                    item.RewardImagePath)).ToList();
        }
        var historyRows = await db.RewardRedemptions.AsNoTracking().Where(item =>
                item.HouseholdId == viewer.HouseholdId && item.HouseholdMemberId == viewer.Id)
            .ToListAsync(cancellationToken);
        var history = historyRows.OrderByDescending(item => item.RequestedAt).Take(30)
            .Select(item => new RewardHistoryItemDto(item.Id, item.RewardName, item.PointsCost,
                item.Status.ToString(), item.RequestedAt, item.PointsBalanceAfterApproval,
                item.RewardImagePath)).ToList();

        return new HouseholdHubDto(memberItems, pets, categoryItems, taskItems, rewardItems, requests, history);
    }

    [HttpPost("members")]
    public async Task<IActionResult> AddMember(int memberId, CreateMemberRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        if (!Enum.TryParse<HouseholdMemberRole>(request.Role, true, out var role) ||
            role is not (HouseholdMemberRole.Parent or HouseholdMemberRole.Child)) return BadRequest(new ApiError("CheckFormFields"));
        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null) return Conflict(new ApiError("EmailInUse"));
        var user = new ApplicationUser { DisplayName = request.DisplayName.Trim(), Email = email, UserName = email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(error => error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" => "EmailInUse",
                "InvalidEmail" or "InvalidUserName" => "InvalidEmail",
                "PasswordTooShort" => "MobilePasswordLength",
                "PasswordRequiresDigit" or "PasswordRequiresLower" or "PasswordRequiresUpper" or
                    "PasswordRequiresNonAlphanumeric" or "PasswordRequiresUniqueChars" => "PasswordRequirements",
                _ => "AccountCreationFailed"
            }).Distinct().ToArray();
            return BadRequest(new ApiError("CheckFormFields", errors));
        }
        var roleResult = await users.AddToRoleAsync(user, role.ToString());
        if (!roleResult.Succeeded)
        {
            await users.DeleteAsync(user);
            return BadRequest(new ApiError("AccountCreationFailed"));
        }
        var newMember = new HouseholdMember
        {
            HouseholdId = parent.HouseholdId, UserId = user.Id, MemberRole = role
        };
        db.HouseholdMembers.Add(newMember);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { await users.DeleteAsync(user); throw; }
        return Ok(new CreatedItemDto(newMember.Id));
    }

    [HttpPost("pets")]
    public async Task<IActionResult> AddPet(int memberId, CreatePetRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        var pet = new Pet { HouseholdId = parent.HouseholdId, Name = request.Name.Trim(),
            Species = request.Species.Trim(), BirthDate = request.BirthDate };
        db.Pets.Add(pet);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new CreatedItemDto(pet.Id));
    }

    [HttpPost("tasks")]
    public async Task<IActionResult> AddTask(int memberId, CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        if (!Enum.TryParse<CareTaskFrequency>(request.Frequency, true, out var frequency) ||
            !Enum.IsDefined(frequency) || request.Points is < 0 or > 1000 ||
            !TaskReminderSettings.IsValid(frequency, request.Reminder)) return BadRequest(new ApiError("CheckFormFields"));
        var pet = await db.Pets.FirstOrDefaultAsync(item => item.Id == request.PetId &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        var assignee = await db.HouseholdMembers.FirstOrDefaultAsync(item =>
            item.Id == request.AssignedMemberId && item.HouseholdId == parent.HouseholdId && item.IsActive,
            cancellationToken);
        if ((request.PetId is not null && pet is null) || assignee is null) return NotFound(new ApiError("NotFound"));
        CareCategory? category = null;
        var custom = request.NewCategoryName?.Trim();
        if (custom is { Length: 1 }) return BadRequest(new ApiError("MobileCategoryNameLength"));
        if (!string.IsNullOrWhiteSpace(custom))
        {
            category = await db.CareCategories.FirstOrDefaultAsync(item => item.HouseholdId == parent.HouseholdId &&
                item.CustomName == custom && item.IsActive, cancellationToken);
            if (category is null)
            {
                category = new CareCategory { HouseholdId = parent.HouseholdId, CustomName = custom };
                db.CareCategories.Add(category);
            }
        }
        else if (request.CategoryId is { } categoryId)
            category = await db.CareCategories.FirstOrDefaultAsync(item => item.Id == categoryId && item.IsActive &&
                (item.HouseholdId == null || item.HouseholdId == parent.HouseholdId), cancellationToken);
        if (category is null && (request.CategoryId is not null || !string.IsNullOrWhiteSpace(custom)))
            return BadRequest(new ApiError("ChooseOrAddCategory"));
        if (string.IsNullOrWhiteSpace(request.Name) && category is null)
            return BadRequest(new ApiError("TaskNameRequired"));
        var task = new CareTask { HouseholdId = parent.HouseholdId, PetId = pet?.Id, Name = request.Name?.Trim(),
            AssignedMemberId = assignee.Id, CareCategory = category, Frequency = frequency,
            PointValue = request.Points };
        TaskReminderSettings.Apply(task, request.Reminder);
        db.CareTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new CreatedItemDto(task.Id));
    }

    [HttpPost("rewards")]
    public async Task<IActionResult> AddReward(int memberId, CreateRewardRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        var ids = request.VisibleToMemberIds.Distinct().ToList();
        var children = await db.HouseholdMembers.Where(item => ids.Contains(item.Id) &&
            item.HouseholdId == parent.HouseholdId && item.MemberRole == HouseholdMemberRole.Child && item.IsActive)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0 || children.Count != ids.Count) return BadRequest(new ApiError("ChooseRewardAudience"));
        var reward = new Reward { HouseholdId = parent.HouseholdId, Name = request.Name.Trim(),
            PointCost = request.PointCost, CreatedAt = rewards.GetUtcNow(),
            VisibleToMembers = children.Select(item => new RewardVisibility { HouseholdMemberId = item.Id }).ToList() };
        db.Rewards.Add(reward);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new CreatedItemDto(reward.Id));
    }

    [HttpPut("members/{id:int}")]
    public async Task<IActionResult> UpdateMember(int memberId, int id, UpdateMemberRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        if (!Enum.TryParse<HouseholdMemberRole>(request.Role, true, out var role) ||
            role is not (HouseholdMemberRole.Parent or HouseholdMemberRole.Child))
            return BadRequest(new ApiError("CheckFormFields"));
        var target = await db.HouseholdMembers.Include(item => item.User).FirstOrDefaultAsync(item =>
            item.Id == id && item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        if (target is null) return NotFound();
        var previousRole = target.MemberRole;
        target.User.DisplayName = request.DisplayName.Trim();
        if (previousRole != role)
        {
            var added = await users.AddToRoleAsync(target.User, role.ToString());
            if (!added.Succeeded) return BadRequest(new ApiError("AccountUpdateFailed"));
            var removed = await users.RemoveFromRoleAsync(target.User, previousRole.ToString());
            if (!removed.Succeeded)
            {
                await users.RemoveFromRoleAsync(target.User, role.ToString());
                return BadRequest(new ApiError("AccountUpdateFailed"));
            }
            target.MemberRole = role;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPut("pets/{id:int}")]
    public async Task<IActionResult> UpdatePet(int memberId, int id, UpdatePetRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        var pet = await db.Pets.FirstOrDefaultAsync(item => item.Id == id &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        if (pet is null) return NotFound();
        pet.Name = request.Name.Trim(); pet.Species = request.Species.Trim(); pet.BirthDate = request.BirthDate;
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPut("tasks/{id:int}")]
    public async Task<IActionResult> UpdateTask(int memberId, int id, UpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        if (!Enum.TryParse<CareTaskFrequency>(request.Frequency, true, out var frequency) ||
            !Enum.IsDefined(frequency) || request.Points is < 0 or > 1000 ||
            !TaskReminderSettings.IsValid(frequency, request.Reminder)) return BadRequest(new ApiError("CheckFormFields"));
        var task = await db.CareTasks.FirstOrDefaultAsync(item => item.Id == id &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        var pet = await db.Pets.FirstOrDefaultAsync(item => item.Id == request.PetId &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        var assignee = await db.HouseholdMembers.FirstOrDefaultAsync(item =>
            item.Id == request.AssignedMemberId && item.HouseholdId == parent.HouseholdId && item.IsActive,
            cancellationToken);
        if (task is null || (request.PetId is not null && pet is null) || assignee is null) return NotFound();
        CareCategory? category = null;
        var custom = request.NewCategoryName?.Trim();
        if (custom is { Length: 1 }) return BadRequest(new ApiError("MobileCategoryNameLength"));
        if (!string.IsNullOrWhiteSpace(custom))
        {
            category = await db.CareCategories.FirstOrDefaultAsync(item => item.HouseholdId == parent.HouseholdId &&
                item.CustomName == custom && item.IsActive, cancellationToken);
            if (category is null)
            {
                category = new CareCategory { HouseholdId = parent.HouseholdId, CustomName = custom };
                db.CareCategories.Add(category);
            }
        }
        else if (request.CategoryId is { } categoryId)
            category = await db.CareCategories.FirstOrDefaultAsync(item => item.Id == categoryId && item.IsActive &&
                (item.HouseholdId == null || item.HouseholdId == parent.HouseholdId), cancellationToken);
        if (category is null && (request.CategoryId is not null || !string.IsNullOrWhiteSpace(custom)))
            return BadRequest(new ApiError("ChooseOrAddCategory"));
        if (string.IsNullOrWhiteSpace(request.Name) && category is null)
            return BadRequest(new ApiError("TaskNameRequired"));
        task.PetId = pet?.Id; task.Name = request.Name?.Trim(); task.AssignedMemberId = assignee.Id;
        task.CareCategoryId = category?.Id; task.CareCategory = category;
        task.Frequency = frequency; task.PointValue = request.Points;
        TaskReminderSettings.Apply(task, request.Reminder);
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPut("media/{kind}/{id:int}"), Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UpdateMedia(int memberId, string kind, int id,
        [FromForm] IFormFile? photo, [FromForm] bool remove, CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        if (photo is null && !remove) return BadRequest(new ApiError("CheckFormFields"));
        string? oldPath;
        string folder;
        string invalidKey;
        string tooLargeKey;
        Action<string?> setPath;
        switch (kind.ToLowerInvariant())
        {
            case "members":
                var target = await db.HouseholdMembers.Include(item => item.User).FirstOrDefaultAsync(item =>
                    item.Id == id && item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
                if (target is null) return NotFound();
                oldPath = target.User.ProfilePhotoPath; folder = "members";
                invalidKey = "InvalidMemberImage"; tooLargeKey = "MemberImageTooLarge";
                setPath = value => target.User.ProfilePhotoPath = value;
                break;
            case "pets":
                var pet = await db.Pets.FirstOrDefaultAsync(item => item.Id == id &&
                    item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
                if (pet is null) return NotFound();
                oldPath = pet.PhotoPath; folder = "pets";
                invalidKey = "InvalidPetImage"; tooLargeKey = "PetImageTooLarge";
                setPath = value => pet.PhotoPath = value;
                break;
            case "tasks":
                var task = await db.CareTasks.FirstOrDefaultAsync(item => item.Id == id &&
                    item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
                if (task is null) return NotFound();
                oldPath = task.ImagePath; folder = "tasks";
                invalidKey = "InvalidTaskImage"; tooLargeKey = "TaskImageTooLarge";
                setPath = value => task.ImagePath = value;
                break;
            case "rewards":
                var reward = await db.Rewards.FirstOrDefaultAsync(item => item.Id == id &&
                    item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
                if (reward is null) return NotFound();
                oldPath = reward.ImagePath; folder = "rewards";
                invalidKey = "InvalidRewardImage"; tooLargeKey = "RewardImageTooLarge";
                setPath = value => reward.ImagePath = value;
                break;
            default: return NotFound();
        }
        string? newPath = null;
        if (photo is not null)
        {
            var saved = await images.SaveAsync(photo, folder, invalidKey, tooLargeKey);
            if (!saved.Success) return BadRequest(new ApiError(saved.ErrorKey!));
            newPath = saved.Path;
        }
        setPath(newPath);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { images.Delete(newPath, folder); throw; }
        if (oldPath != newPath) images.Delete(oldPath, folder);
        return Ok();
    }

    [HttpDelete("members/{id:int}")]
    public async Task<IActionResult> ArchiveMember(int memberId, int id, CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        var target = await db.HouseholdMembers.FirstOrDefaultAsync(item => item.Id == id &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        if (target is null) return NotFound();
        if (target.UserId == parent.UserId) return BadRequest(new ApiError("CannotDeleteYourself"));
        target.IsActive = false;
        await db.CareTasks.Where(item => item.AssignedMemberId == target.Id && item.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpDelete("pets/{id:int}")]
    public async Task<IActionResult> ArchivePet(int memberId, int id, CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        var pet = await db.Pets.FirstOrDefaultAsync(item => item.Id == id &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        if (pet is null) return NotFound();
        pet.IsActive = false;
        await db.CareTasks.Where(item => item.PetId == pet.Id && item.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> ArchiveTask(int memberId, int id, CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        var task = await db.CareTasks.FirstOrDefaultAsync(item => item.Id == id &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        if (task is null) return NotFound();
        task.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("rewards/{rewardId:int}/request")]
    public async Task<IActionResult> RequestReward(int memberId, int rewardId, CancellationToken cancellationToken)
    {
        var child = await GetMemberAsync(memberId, cancellationToken);
        if (child?.MemberRole != HouseholdMemberRole.Child) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var reward = await db.Rewards.Include(item => item.VisibleToMembers).FirstOrDefaultAsync(item =>
            item.Id == rewardId && item.HouseholdId == child.HouseholdId && item.IsActive &&
            item.VisibleToMembers.Any(link => link.HouseholdMemberId == child.Id), cancellationToken);
        if (reward is null) return NotFound();
        if (await db.RewardRedemptions.AnyAsync(item => item.HouseholdId == child.HouseholdId &&
            item.HouseholdMemberId == child.Id && item.RewardId == rewardId &&
            item.Status == RewardRedemptionStatus.Pending, cancellationToken))
            return Conflict(new ApiError("RewardAlreadyRequested"));
        var balance = await points.GetBalanceAsync(child.HouseholdId, child.UserId, cancellationToken);
        if (balance < reward.PointCost) return BadRequest(new ApiError("NotEnoughPointsForReward"));
        db.RewardRedemptions.Add(rewards.CreateRequest(reward, child, balance));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("reward-requests/{id:int}/review")]
    public async Task<IActionResult> ReviewReward(int memberId, int id, RewardDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var redemption = await db.RewardRedemptions.Include(item => item.HouseholdMember)
            .FirstOrDefaultAsync(item => item.Id == id && item.HouseholdId == parent.HouseholdId &&
                item.Status == RewardRedemptionStatus.Pending, cancellationToken);
        if (redemption is null) return NotFound();
        var balance = await points.GetBalanceAsync(parent.HouseholdId, redemption.HouseholdMember.UserId,
            cancellationToken);
        if (request.Approve && balance < redemption.PointsCost)
            return BadRequest(new ApiError("NotEnoughPointsForApproval"));
        rewards.Review(redemption, parent, request.Approve ? RewardRedemptionStatus.Approved :
            RewardRedemptionStatus.Rejected, balance);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("rewards/purchase")]
    public async Task<IActionResult> PurchaseReward(int memberId, DirectRewardRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await GetParentAsync(memberId, cancellationToken);
        if (parent is null) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var reward = await db.Rewards.FirstOrDefaultAsync(item => item.Id == request.RewardId &&
            item.HouseholdId == parent.HouseholdId && item.IsActive, cancellationToken);
        var child = await db.HouseholdMembers.FirstOrDefaultAsync(item => item.Id == request.ChildMemberId &&
            item.HouseholdId == parent.HouseholdId && item.MemberRole == HouseholdMemberRole.Child && item.IsActive,
            cancellationToken);
        if (reward is null || child is null) return NotFound();
        var balance = await points.GetBalanceAsync(parent.HouseholdId, child.UserId, cancellationToken);
        if (balance < reward.PointCost) return BadRequest(new ApiError("NotEnoughPointsForPurchase"));
        db.RewardRedemptions.Add(rewards.CreateDirectPurchase(reward, child, parent, balance));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok();
    }

    private async Task<HouseholdMember?> GetParentAsync(int id, CancellationToken cancellationToken)
    {
        var member = await GetMemberAsync(id, cancellationToken);
        return member?.MemberRole == HouseholdMemberRole.Parent ? member : null;
    }

    private async Task<HouseholdMember?> GetMemberAsync(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return null;
        var member = await db.HouseholdMembers.FirstOrDefaultAsync(item =>
            item.Id == id && item.UserId == user.Id && item.IsActive, cancellationToken);
        return member is not null && await users.IsInRoleAsync(user, member.MemberRole.ToString()) ? member : null;
    }

    private string CategoryName(CareCategory? category) => category?.Code is { } code
        ? localizer[$"Category_{code}"].Value : category?.CustomName ?? localizer["GeneralTask"].Value;
    private static string TaskImage(CareTask task) => task.ImagePath ?? (task.CareCategory?.Code is
        "Feeding" or "Water" or "Cleaning" or "Playing" or "Health"
            ? $"/images/tasks/{task.CareCategory!.Code!.ToLowerInvariant()}.webp"
            : task.Pet?.PhotoPath ?? "/images/tasks/general.svg");
}

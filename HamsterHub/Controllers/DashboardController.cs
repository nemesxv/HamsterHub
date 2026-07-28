using HamsterHub.Data;
using HamsterHub.Models;
using HamsterHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Data;

namespace HamsterHub.Controllers;

[Authorize]
public class DashboardController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer,
    IWebHostEnvironment environment,
    CareLogService careLogService,
    PointBalanceService pointBalanceService,
    RewardService rewardService) : Controller
{
    private const long MaximumPetPhotoBytes = 5 * 1024 * 1024;
    private const int MaximumCompletionPhotos = 8;
    private static readonly IReadOnlyDictionary<string, string> DefaultTaskImages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Feeding"] = "/images/tasks/feeding.webp",
            ["Water"] = "/images/tasks/water.webp",
            ["Cleaning"] = "/images/tasks/cleaning.webp",
            ["Playing"] = "/images/tasks/playing.webp",
            ["Health"] = "/images/tasks/health.webp"
        };
    private static readonly HashSet<string> AllowedPhotoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly HashSet<string> AllowedPhotoContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public IActionResult Index()
    {
        if (User.IsInRole("Parent"))
        {
            return RedirectToAction(nameof(Parent));
        }

        if (User.IsInRole("Child"))
        {
            return RedirectToAction(nameof(Child));
        }

        return Forbid();
    }

    [Authorize(Roles = "Parent")]
    public async Task<IActionResult> Parent()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var membership = await GetOrCreateParentMembershipAsync(user);
        var householdId = membership.HouseholdId;
        var careTaskEntities = await dbContext.CareTasks
            .AsNoTracking()
            .Include(task => task.Pet)
            .Include(task => task.CareCategory)
            .Include(task => task.AssignedMember)
                .ThenInclude(member => member.User)
            .Where(task => task.HouseholdId == householdId && task.IsActive)
            .OrderBy(task => task.Pet.Name)
            .ToListAsync();
        var pendingEntities = await dbContext.CareLogs
            .AsNoTracking()
            .Include(log => log.Photos)
            .Include(log => log.CompletedByUser)
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Where(log =>
                log.Pet.HouseholdId == householdId &&
                log.CareTask.HouseholdId == householdId &&
                log.CareTask.PetId == log.PetId &&
                log.Status == CareLogStatus.Pending)
            .OrderByDescending(log => log.CompletedAt)
            .ToListAsync();
        var categoryEntities = await dbContext.CareCategories
            .AsNoTracking()
            .Where(category =>
                category.IsActive &&
                (category.HouseholdId == null || category.HouseholdId == householdId))
            .OrderBy(category => category.HouseholdId)
            .ThenBy(category => category.CustomName)
            .ToListAsync();
        var rewardEntities = await dbContext.Rewards
            .AsNoTracking()
            .Include(reward => reward.VisibleToMembers)
                .ThenInclude(visibility => visibility.HouseholdMember)
                    .ThenInclude(member => member.User)
            .Where(reward => reward.HouseholdId == householdId && reward.IsActive)
            .OrderBy(reward => reward.Name)
            .ToListAsync();
        var pendingRewardEntities = await dbContext.RewardRedemptions
            .AsNoTracking()
            .Include(redemption => redemption.HouseholdMember)
                .ThenInclude(member => member.User)
            .Where(redemption =>
                redemption.HouseholdId == householdId &&
                redemption.HouseholdMember.HouseholdId == householdId &&
                redemption.Status == RewardRedemptionStatus.Pending)
            .OrderByDescending(redemption => redemption.RequestedAt)
            .ToListAsync();
        var rewardChildren = await dbContext.HouseholdMembers
            .AsNoTracking()
            .Include(member => member.User)
            .Where(member =>
                member.HouseholdId == householdId &&
                member.MemberRole == HouseholdMemberRole.Child &&
                member.IsActive)
            .OrderBy(member => member.User.DisplayName)
            .ToListAsync();
        var rewardChildBalances = await pointBalanceService.GetBalancesAsync(
            householdId, rewardChildren.Select(member => member.UserId));

        var model = new ParentDashboardViewModel
        {
            ParentName = user.DisplayName,
            HouseholdName = GetHouseholdDisplayName(membership.Household),
            Members = await dbContext.HouseholdMembers
                .AsNoTracking()
                .Where(member => member.HouseholdId == householdId && member.IsActive)
                .OrderBy(member => member.MemberRole)
                .ThenBy(member => member.User.DisplayName)
                .Select(member => new FamilyMemberSummary(
                    member.Id,
                    member.UserId,
                    member.User.DisplayName,
                    member.User.Email ?? "",
                    member.MemberRole,
                    member.UserId == user.Id,
                    member.User.ProfilePhotoPath))
                .ToListAsync(),
            Pets = await dbContext.Pets
                .AsNoTracking()
                .Where(pet => pet.HouseholdId == householdId && pet.IsActive)
                .OrderBy(pet => pet.Name)
                .Select(pet => new PetSummary(
                    pet.Id, pet.Name, pet.Species, pet.BirthDate, pet.PhotoPath))
                .ToListAsync(),
            CareCategories = categoryEntities
                .Select(category => new CareCategoryOption(category.Id, GetCategoryName(category)))
                .ToList(),
            CareTasks = careTaskEntities
                .Select(task => new CareTaskSummary(
                    task.Id,
                    task.PetId,
                    task.Pet.Name,
                    task.Pet.PhotoPath,
                    task.CareCategoryId,
                    GetCategoryName(task.CareCategory),
                    task.AssignedMemberId,
                    task.AssignedMember.User.DisplayName,
                    task.Frequency,
                    task.PointValue,
                    GetTaskImagePath(task),
                    task.ImagePath is not null,
                    task.AssignedMember.UserId == user.Id))
                .ToList(),
            PendingCare = pendingEntities
                .Select(log => new PendingCareSummary(
                    log.Id,
                    log.CompletedByUser.DisplayName,
                    log.Pet.Name,
                    GetCategoryName(log.CareTask.CareCategory),
                    log.PointsAwarded,
                    log.CompletedAt,
                    log.Photos.Select(photo => photo.ImagePath).ToList()))
                .ToList(),
            Rewards = rewardEntities
                .Select(reward => new ParentRewardSummary(
                    reward.Id,
                    reward.Name,
                    reward.PointCost,
                    reward.ImagePath,
                    reward.VisibleToMembers
                        .Where(item =>
                            item.HouseholdMember.IsActive &&
                            item.HouseholdMember.MemberRole == HouseholdMemberRole.Child)
                        .Select(item => item.HouseholdMember.User.DisplayName)
                        .OrderBy(name => name)
                        .ToList()))
                .ToList(),
            PendingRewards = pendingRewardEntities
                .Select(redemption => new PendingRewardSummary(
                    redemption.Id,
                    redemption.RewardName,
                    redemption.RewardImagePath,
                    redemption.HouseholdMember.User.DisplayName,
                    redemption.PointsCost,
                    redemption.RequestedAt))
                .ToList(),
            RewardChildren = rewardChildren
                .Select(child => new RewardChildOption(
                    child.Id,
                    child.User.DisplayName,
                    rewardChildBalances[child.UserId]))
                .ToList()
        };
        model.AddCareTask.PetId = GetRememberedSelection(
            "LastTaskPetId", model.Pets.Select(pet => pet.Id));
        model.AddCareTask.AssignedMemberId = GetRememberedSelection(
            "LastTaskMemberId", model.Members.Select(member => member.Id));

        return View(model);
    }

    [Authorize(Roles = "Child")]
    public async Task<IActionResult> Child()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var membership = await dbContext.HouseholdMembers
            .AsNoTracking()
            .Include(member => member.Household)
            .FirstOrDefaultAsync(member =>
                member.UserId == user.Id &&
                member.MemberRole == HouseholdMemberRole.Child &&
                member.IsActive);

        if (membership is null)
        {
            return View(new KidDashboardViewModel { ChildName = user.DisplayName });
        }

        var householdId = membership.HouseholdId;
        var careTaskEntities = await dbContext.CareTasks
            .AsNoTracking()
            .Include(task => task.Pet)
            .Include(task => task.CareCategory)
            .Include(task => task.AssignedMember)
            .Where(task =>
                task.HouseholdId == householdId &&
                task.AssignedMemberId == membership.Id &&
                task.IsActive &&
                task.Pet.IsActive &&
                task.AssignedMember.IsActive)
            .OrderBy(task => task.Pet.Name)
            .ToListAsync();
        var recentCareEntities = await dbContext.CareLogs
            .AsNoTracking()
            .Include(log => log.Photos)
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Where(log =>
                log.CompletedByUserId == user.Id &&
                log.Pet.HouseholdId == householdId &&
                log.CareTask.HouseholdId == householdId &&
                log.CareTask.PetId == log.PetId)
            .OrderByDescending(log => log.CompletedAt)
            .Take(8)
            .ToListAsync();
        var visibleFamilyMemberEntities = await dbContext.HouseholdMembers
            .AsNoTracking()
            .Include(member => member.User)
            .Where(member =>
                member.HouseholdId == householdId &&
                member.Id != membership.Id &&
                member.IsActive &&
                (member.MemberRole == HouseholdMemberRole.Parent ||
                 (membership.CanViewOtherChildrenHistory &&
                  member.MemberRole == HouseholdMemberRole.Child &&
                  member.ShareHistoryWithChildren)))
            .OrderBy(member => member.MemberRole)
            .ThenBy(member => member.User.DisplayName)
            .ToListAsync();
        var rewardEntities = await dbContext.Rewards
            .AsNoTracking()
            .Where(reward =>
                reward.HouseholdId == householdId &&
                reward.IsActive &&
                reward.VisibleToMembers.Any(visibility =>
                    visibility.HouseholdMemberId == membership.Id &&
                    visibility.HouseholdMember.HouseholdId == householdId &&
                    visibility.HouseholdMember.IsActive))
            .OrderBy(reward => reward.Name)
            .ToListAsync();
        var rewardRedemptionEntities = await dbContext.RewardRedemptions
            .AsNoTracking()
            .Where(redemption =>
                redemption.HouseholdId == householdId &&
                redemption.HouseholdMemberId == membership.Id &&
                redemption.HouseholdMember.HouseholdId == householdId)
            .OrderByDescending(redemption => redemption.RequestedAt)
            .Take(8)
            .ToListAsync();
        var visibleUserIds = visibleFamilyMemberEntities
            .Select(member => member.UserId)
            .Append(user.Id);
        var visibleBalances = await pointBalanceService.GetBalancesAsync(
            householdId, visibleUserIds);
        var pendingRewardIds = rewardRedemptionEntities
            .Where(redemption => redemption.Status == RewardRedemptionStatus.Pending)
            .Select(redemption => redemption.RewardId)
            .ToHashSet();
        var currentBalance = visibleBalances[user.Id];

        return View(new KidDashboardViewModel
        {
            ChildName = user.DisplayName,
            HouseholdName = GetHouseholdDisplayName(membership.Household),
            CurrentPoints = currentBalance,
            PendingCount = await dbContext.CareLogs.CountAsync(log =>
                log.CompletedByUserId == user.Id &&
                log.Pet.HouseholdId == householdId &&
                log.CareTask.HouseholdId == householdId &&
                log.CareTask.PetId == log.PetId &&
                log.Status == CareLogStatus.Pending),
            Pets = await dbContext.Pets
                .AsNoTracking()
                .Where(pet => pet.HouseholdId == householdId && pet.IsActive)
                .OrderBy(pet => pet.Name)
                .Select(pet => new PetSummary(
                    pet.Id, pet.Name, pet.Species, pet.BirthDate, pet.PhotoPath))
                .ToListAsync(),
            CareTasks = careTaskEntities
                .Select(task => new CareTaskSummary(
                    task.Id,
                    task.PetId,
                    task.Pet.Name,
                    task.Pet.PhotoPath,
                    task.CareCategoryId,
                    GetCategoryName(task.CareCategory),
                    task.AssignedMemberId,
                    user.DisplayName,
                    task.Frequency,
                    task.PointValue,
                    GetTaskImagePath(task),
                    task.ImagePath is not null,
                    true))
                .ToList(),
            RecentCare = recentCareEntities
                .Select(log => new KidCareHistorySummary(
                    log.Pet.Name,
                    log.Pet.PhotoPath,
                    GetCategoryName(log.CareTask.CareCategory),
                    log.PointsAwarded,
                    log.PointsTotalAfterApproval,
                    log.Status,
                    log.CompletedAt,
                    log.ApprovedAt,
                    GetCareLogImagePath(log),
                    log.Photos
                        .OrderBy(photo => photo.CreatedAt)
                        .Select(photo => photo.ImagePath)
                        .ToList()))
                .ToList(),
            FamilyMembers = visibleFamilyMemberEntities
                .Select(member => new KidFamilyMemberSummary(
                    member.Id,
                    member.User.DisplayName,
                    member.MemberRole,
                    member.User.ProfilePhotoPath,
                    visibleBalances[member.UserId]))
                .ToList(),
            Rewards = rewardEntities
                .Select(reward => new KidRewardSummary(
                    reward.Id,
                    reward.Name,
                    reward.PointCost,
                    reward.ImagePath,
                    currentBalance >= reward.PointCost,
                    pendingRewardIds.Contains(reward.Id)))
                .ToList(),
            RewardRedemptions = rewardRedemptionEntities
                .Select(redemption => new KidRewardRedemptionSummary(
                    redemption.RewardName,
                    redemption.RewardImagePath,
                    redemption.PointsCost,
                    redemption.Status,
                    redemption.RequestedAt,
                    redemption.PointsBalanceAfterApproval))
                .ToList()
        });
    }

    [Authorize(Roles = "Parent")]
    public async Task<IActionResult> FamilyMember(int id)
    {
        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var member = await dbContext.HouseholdMembers
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.HouseholdId == membership.HouseholdId &&
                item.IsActive);
        if (member is null)
        {
            return NotFound();
        }

        var logs = await dbContext.CareLogs
            .AsNoTracking()
            .Include(log => log.Photos)
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Include(log => log.ApprovedByUser)
            .Where(log =>
                log.CompletedByUserId == member.UserId &&
                log.Pet.HouseholdId == membership.HouseholdId &&
                log.CareTask.HouseholdId == membership.HouseholdId &&
                log.CareTask.PetId == log.PetId)
            .ToListAsync();
        var rewardRedemptions = await dbContext.RewardRedemptions
            .AsNoTracking()
            .Include(redemption => redemption.ReviewedByUser)
            .Where(redemption =>
                redemption.HouseholdId == membership.HouseholdId &&
                redemption.HouseholdMemberId == member.Id &&
                redemption.HouseholdMember.HouseholdId == membership.HouseholdId &&
                redemption.Status == RewardRedemptionStatus.Approved)
            .ToListAsync();
        var history = logs
            .Select(log => new MemberPointHistorySummary(
                MemberPointHistoryKind.Care,
                GetCategoryName(log.CareTask.CareCategory),
                log.Pet.Name,
                log.Status == CareLogStatus.Approved ? log.PointsAwarded : 0,
                log.PointsTotalAfterApproval,
                $"Status_{log.Status}",
                log.Status.ToString().ToLowerInvariant(),
                log.CompletedAt,
                log.ApprovedAt,
                log.ApprovedByUser?.DisplayName,
                GetCareLogImagePath(log),
                log.Photos.Select(photo => photo.ImagePath).ToList()))
            .Concat(rewardRedemptions.Select(redemption =>
                new MemberPointHistorySummary(
                    MemberPointHistoryKind.Reward,
                    redemption.RewardName,
                    localizer["RewardRedeemed"].Value,
                    -redemption.PointsCost,
                    redemption.PointsBalanceAfterApproval,
                    $"RewardStatus_{redemption.Status}",
                    redemption.Status.ToString().ToLowerInvariant(),
                    redemption.RequestedAt,
                    redemption.ReviewedAt,
                    redemption.ReviewedByUser?.DisplayName,
                    redemption.RewardImagePath,
                    [])))
            .OrderByDescending(item => item.ReviewedAt ?? item.RecordedAt)
            .ToList();

        return View(new FamilyMemberHistoryViewModel
        {
            MemberId = member.Id,
            DisplayName = member.User.DisplayName,
            Email = member.User.Email ?? string.Empty,
            PhotoPath = member.User.ProfilePhotoPath,
            Role = member.MemberRole,
            CurrentPoints = await pointBalanceService.GetBalanceAsync(
                membership.HouseholdId, member.UserId),
            CanViewOtherChildrenHistory = member.CanViewOtherChildrenHistory,
            ShareHistoryWithChildren = member.ShareHistoryWithChildren,
            History = history
        });
    }

    [Authorize(Roles = "Child")]
    public async Task<IActionResult> ChildFamilyMember(int id)
    {
        var viewer = await GetCurrentMembershipAsync(HouseholdMemberRole.Child);
        if (viewer is null)
        {
            return Forbid();
        }

        var member = await dbContext.HouseholdMembers
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.Id != viewer.Id &&
                item.HouseholdId == viewer.HouseholdId &&
                item.IsActive);
        if (member is null)
        {
            return NotFound();
        }

        var canView = member.MemberRole == HouseholdMemberRole.Parent ||
            (member.MemberRole == HouseholdMemberRole.Child &&
             viewer.CanViewOtherChildrenHistory &&
             member.ShareHistoryWithChildren);
        if (!canView)
        {
            return Forbid();
        }

        var logs = await dbContext.CareLogs
            .AsNoTracking()
            .Include(log => log.Photos)
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Where(log =>
                log.CompletedByUserId == member.UserId &&
                log.Pet.HouseholdId == viewer.HouseholdId &&
                log.CareTask.HouseholdId == viewer.HouseholdId &&
                log.CareTask.PetId == log.PetId &&
                log.Status == CareLogStatus.Approved)
            .OrderByDescending(log => log.CompletedAt)
            .Take(30)
            .ToListAsync();

        return View(new KidFamilyMemberHistoryViewModel
        {
            MemberId = member.Id,
            DisplayName = member.User.DisplayName,
            Role = member.MemberRole,
            PhotoPath = member.User.ProfilePhotoPath,
            CurrentPoints = await pointBalanceService.GetBalanceAsync(
                viewer.HouseholdId, member.UserId),
            History = logs.Select(log => new KidSharedCareHistorySummary(
                log.Pet.Name,
                GetCategoryName(log.CareTask.CareCategory),
                log.CompletedAt,
                GetCareLogImagePath(log),
                log.Photos
                    .OrderBy(photo => photo.CreatedAt)
                    .Select(photo => photo.ImagePath)
                    .ToList())).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> UpdateHistoryPrivacy(UpdateHistoryPrivacyInput input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var child = await dbContext.HouseholdMembers.FirstOrDefaultAsync(item =>
            item.Id == input.MemberId &&
            item.HouseholdId == membership.HouseholdId &&
            item.MemberRole == HouseholdMemberRole.Child &&
            item.IsActive);
        if (child is null)
        {
            return NotFound();
        }

        child.CanViewOtherChildrenHistory = input.CanViewOtherChildrenHistory;
        child.ShareHistoryWithChildren = input.ShareHistoryWithChildren;
        await dbContext.SaveChangesAsync();
        TempData["StatusMessage"] = localizer["PrivacyUpdated"].Value;
        return RedirectToAction(nameof(FamilyMember), new { id = child.Id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> AddMember([Bind(Prefix = "AddMember")] AddMemberInput input)
    {
        if (!ModelState.IsValid ||
            input.Role is not (HouseholdMemberRole.Parent or HouseholdMemberRole.Child))
        {
            TempData["StatusMessage"] = localizer["CheckFormFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var parent = await userManager.GetUserAsync(User);
        if (parent is null)
        {
            return Challenge();
        }

        var membership = await GetOrCreateParentMembershipAsync(parent);
        var email = input.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            TempData["StatusMessage"] = localizer["EmailInUse"].Value;
            return RedirectToAction(nameof(Parent));
        }

        string? photoPath = null;
        if (input.Photo is not null)
        {
            var photoResult = await SaveUploadedImageAsync(
                input.Photo, "members", "InvalidMemberImage", "MemberImageTooLarge");
            if (!photoResult.Success)
            {
                TempData["StatusMessage"] = localizer[photoResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Parent));
            }

            photoPath = photoResult.Path;
        }

        var newUser = new ApplicationUser
        {
            DisplayName = input.DisplayName.Trim(),
            Email = email,
            UserName = email,
            ProfilePhotoPath = photoPath
        };
        var createResult = await userManager.CreateAsync(newUser, input.Password);
        if (!createResult.Succeeded)
        {
            DeleteUploadedImage(photoPath, "members");
            TempData["StatusMessage"] = localizer["PasswordRequirements"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var roleName = input.Role == HouseholdMemberRole.Parent ? "Parent" : "Child";
        var roleResult = await userManager.AddToRoleAsync(newUser, roleName);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(newUser);
            DeleteUploadedImage(photoPath, "members");
            TempData["StatusMessage"] = localizer["AccountCreationFailed"].Value;
            return RedirectToAction(nameof(Parent));
        }

        dbContext.HouseholdMembers.Add(new HouseholdMember
        {
            HouseholdId = membership.HouseholdId,
            UserId = newUser.Id,
            MemberRole = input.Role
        });
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            await userManager.DeleteAsync(newUser);
            DeleteUploadedImage(photoPath, "members");
            throw;
        }

        TempData["StatusMessage"] = localizer["FamilyMemberAdded", input.DisplayName].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> AddPet([Bind(Prefix = "AddPet")] AddPetInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = localizer["CheckFormFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        string? photoPath = null;
        if (input.Photo is not null)
        {
            var extension = Path.GetExtension(input.Photo.FileName);
            if (input.Photo.Length == 0 ||
                !AllowedPhotoExtensions.Contains(extension) ||
                !AllowedPhotoContentTypes.Contains(input.Photo.ContentType))
            {
                TempData["StatusMessage"] = localizer["InvalidPetImage"].Value;
                return RedirectToAction(nameof(Parent));
            }

            if (input.Photo.Length > MaximumPetPhotoBytes)
            {
                TempData["StatusMessage"] = localizer["PetImageTooLarge"].Value;
                return RedirectToAction(nameof(Parent));
            }

            var uploadDirectory = Path.Combine(environment.WebRootPath, "uploads", "pets");
            Directory.CreateDirectory(uploadDirectory);
            var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            await using var stream = System.IO.File.Create(Path.Combine(uploadDirectory, fileName));
            await input.Photo.CopyToAsync(stream);
            photoPath = $"/uploads/pets/{fileName}";
        }

        dbContext.Pets.Add(new Pet
        {
            HouseholdId = membership.HouseholdId,
            Name = input.Name.Trim(),
            Species = input.Species.Trim(),
            BirthDate = input.BirthDate,
            PhotoPath = photoPath
        });
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            if (photoPath is not null)
            {
                System.IO.File.Delete(Path.Combine(
                    environment.WebRootPath,
                    photoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            }

            throw;
        }

        TempData["StatusMessage"] = localizer["PetAdded", input.Name].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> AddCareTask([Bind(Prefix = "AddCareTask")] AddCareTaskInput input)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(input.Frequency))
        {
            TempData["StatusMessage"] = localizer["CheckFormFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var pet = await dbContext.Pets.FirstOrDefaultAsync(item =>
            item.Id == input.PetId &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        if (pet is null)
        {
            return Forbid();
        }
        var assignedMember = await dbContext.HouseholdMembers.FirstOrDefaultAsync(item =>
            item.Id == input.AssignedMemberId &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        if (assignedMember is null)
        {
            return Forbid();
        }

        CareCategory? category;
        var newCategoryName = input.NewCategoryName?.Trim();
        if (!string.IsNullOrWhiteSpace(newCategoryName))
        {
            category = await dbContext.CareCategories.FirstOrDefaultAsync(item =>
                item.HouseholdId == membership.HouseholdId &&
                item.CustomName == newCategoryName &&
                item.IsActive);
            if (category is null)
            {
                category = new CareCategory
                {
                    HouseholdId = membership.HouseholdId,
                    CustomName = newCategoryName
                };
                dbContext.CareCategories.Add(category);
            }
        }
        else if (input.CategoryId is not null)
        {
            category = await dbContext.CareCategories.FirstOrDefaultAsync(item =>
                item.Id == input.CategoryId &&
                item.IsActive &&
                (item.HouseholdId == null || item.HouseholdId == membership.HouseholdId));
        }
        else
        {
            category = null;
        }

        if (category is null)
        {
            TempData["StatusMessage"] = localizer["ChooseOrAddCategory"].Value;
            return RedirectToAction(nameof(Parent));
        }

        string? imagePath = null;
        if (input.Image is not null)
        {
            var imageResult = await SaveUploadedImageAsync(
                input.Image, "tasks", "InvalidTaskImage", "TaskImageTooLarge");
            if (!imageResult.Success)
            {
                TempData["StatusMessage"] = localizer[imageResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Parent));
            }

            imagePath = imageResult.Path;
        }

        dbContext.CareTasks.Add(new CareTask
        {
            HouseholdId = membership.HouseholdId,
            PetId = pet.Id,
            AssignedMemberId = assignedMember.Id,
            CareCategory = category,
            Frequency = input.Frequency,
            PointValue = input.PointValue,
            ImagePath = imagePath
        });
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            DeleteUploadedImage(imagePath, "tasks");
            throw;
        }
        RememberTaskSelections(pet.Id, assignedMember.Id);

        TempData["StatusMessage"] = localizer["CareTaskAdded", GetCategoryName(category)].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Child")]
    public async Task<IActionResult> CompleteTask(int careTaskId, List<IFormFile>? photos)
    {
        var user = await userManager.GetUserAsync(User);
        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Child);
        if (user is null || membership is null)
        {
            return Forbid();
        }

        var careTask = await dbContext.CareTasks
            .Include(item => item.Pet)
            .Include(item => item.AssignedMember)
            .FirstOrDefaultAsync(item =>
            item.Id == careTaskId &&
            item.HouseholdId == membership.HouseholdId &&
            item.AssignedMemberId == membership.Id &&
            item.IsActive &&
            item.Pet.IsActive &&
            item.AssignedMember.IsActive);

        if (careTask is null ||
            !careLogService.CanCompleteTask(careTask, membership))
        {
            return Forbid();
        }

        var now = careLogService.GetUtcNow();
        var earliestAllowed = careLogService.GetEarliestAllowed(
            careTask.Frequency, now);

        if (earliestAllowed is not null &&
            await dbContext.CareLogs.AnyAsync(log =>
                log.PetId == careTask.PetId &&
                log.CareTaskId == careTaskId &&
                log.CompletedByUserId == user.Id &&
                log.Status != CareLogStatus.Rejected &&
                log.CompletedAt >= earliestAllowed))
        {
            TempData["StatusMessage"] = localizer["TaskAlreadyRecorded"].Value;
            return RedirectToAction(nameof(Child));
        }

        photos ??= [];
        if (photos.Count > MaximumCompletionPhotos)
        {
            TempData["StatusMessage"] = localizer["TooManyTaskPhotos", MaximumCompletionPhotos].Value;
            return RedirectToAction(nameof(Child));
        }

        var savedPhotoPaths = new List<string>();
        foreach (var photo in photos)
        {
            var photoResult = await SaveUploadedImageAsync(
                photo, "care-logs", "InvalidTaskPhoto", "TaskPhotoTooLarge");
            if (!photoResult.Success)
            {
                foreach (var savedPath in savedPhotoPaths)
                {
                    DeleteUploadedImage(savedPath, "care-logs");
                }

                TempData["StatusMessage"] = localizer[photoResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Child));
            }

            savedPhotoPaths.Add(photoResult.Path!);
        }

        var careLog = careLogService.CreateChildCompletion(
            careTask, membership, now);
        careLog.Photos = savedPhotoPaths
            .Select(path => new CareLogPhoto { ImagePath = path })
            .ToList();
        dbContext.CareLogs.Add(careLog);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            foreach (var savedPath in savedPhotoPaths)
            {
                DeleteUploadedImage(savedPath, "care-logs");
            }

            throw;
        }

        TempData["StatusMessage"] = localizer["TaskSentForApproval"].Value;
        return RedirectToAction(nameof(Child));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> CompleteTaskAsParent(int careTaskId)
    {
        var parent = await userManager.GetUserAsync(User);
        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (parent is null || membership is null)
        {
            return Forbid();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var careTask = await dbContext.CareTasks
            .Include(item => item.Pet)
            .Include(item => item.AssignedMember)
            .FirstOrDefaultAsync(item =>
                item.Id == careTaskId &&
                item.HouseholdId == membership.HouseholdId &&
                item.AssignedMemberId == membership.Id &&
                item.IsActive &&
                item.Pet.IsActive &&
                item.AssignedMember.IsActive);
        if (careTask is null ||
            !careLogService.CanCompleteTask(careTask, membership))
        {
            return Forbid();
        }

        var now = careLogService.GetUtcNow();
        var earliestAllowed = careLogService.GetEarliestAllowed(
            careTask.Frequency, now);
        if (earliestAllowed is not null &&
            await dbContext.CareLogs.AnyAsync(log =>
                log.PetId == careTask.PetId &&
                log.CareTaskId == careTask.Id &&
                log.CompletedByUserId == parent.Id &&
                log.Status != CareLogStatus.Rejected &&
                log.CompletedAt >= earliestAllowed))
        {
            TempData["StatusMessage"] = localizer["TaskAlreadyRecorded"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var previousTotal = await pointBalanceService.GetBalanceAsync(
            membership.HouseholdId, parent.Id);
        dbContext.CareLogs.Add(careLogService.CreateParentCompletion(
            careTask, membership, previousTotal, now));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["StatusMessage"] = localizer["ParentTaskCompleted"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> ReviewCareLog(int id, CareLogStatus decision)
    {
        if (decision is not (CareLogStatus.Approved or CareLogStatus.Rejected))
        {
            return BadRequest();
        }

        var parent = await userManager.GetUserAsync(User);
        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (parent is null || membership is null)
        {
            return Forbid();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var careLog = await dbContext.CareLogs
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
            .FirstOrDefaultAsync(log =>
                log.Id == id &&
                log.Pet.HouseholdId == membership.HouseholdId &&
                log.CareTask.HouseholdId == membership.HouseholdId &&
                log.CareTask.PetId == log.PetId &&
                log.Status == CareLogStatus.Pending);

        if (careLog is null ||
            !careLogService.CanReviewCareLog(careLog, membership))
        {
            return NotFound();
        }

        var previousTotal = decision == CareLogStatus.Approved
            ? await pointBalanceService.GetBalanceAsync(
                membership.HouseholdId, careLog.CompletedByUserId)
            : 0;
        careLogService.ReviewCareLog(
            careLog,
            membership,
            decision,
            previousTotal,
            careLogService.GetUtcNow());
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["StatusMessage"] = localizer[
            decision == CareLogStatus.Approved ? "CareApproved" : "CareRejected"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> AddReward(
        [Bind(Prefix = "AddReward")] AddRewardInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = localizer["CheckRewardFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var visibleMemberIds = input.VisibleToMemberIds.Distinct().ToList();
        var children = await dbContext.HouseholdMembers
            .Where(member =>
                visibleMemberIds.Contains(member.Id) &&
                member.HouseholdId == membership.HouseholdId &&
                member.MemberRole == HouseholdMemberRole.Child &&
                member.IsActive)
            .ToListAsync();
        if (children.Count != visibleMemberIds.Count || children.Count == 0)
        {
            return Forbid();
        }

        string? imagePath = null;
        if (input.Image is not null)
        {
            var imageResult = await SaveUploadedImageAsync(
                input.Image, "rewards", "InvalidRewardImage", "RewardImageTooLarge");
            if (!imageResult.Success)
            {
                TempData["StatusMessage"] = localizer[imageResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Parent));
            }

            imagePath = imageResult.Path;
        }

        var reward = new Reward
        {
            HouseholdId = membership.HouseholdId,
            Name = input.Name.Trim(),
            PointCost = input.PointCost,
            ImagePath = imagePath,
            CreatedAt = rewardService.GetUtcNow(),
            VisibleToMembers = children
                .Select(child => new RewardVisibility { HouseholdMemberId = child.Id })
                .ToList()
        };
        dbContext.Rewards.Add(reward);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            DeleteUploadedImage(imagePath, "rewards");
            throw;
        }

        TempData["StatusMessage"] = localizer["RewardAdded", reward.Name].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Child")]
    public async Task<IActionResult> RequestReward(int rewardId)
    {
        var child = await GetCurrentMembershipAsync(HouseholdMemberRole.Child);
        if (child is null)
        {
            return Forbid();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var reward = await dbContext.Rewards
            .Include(item => item.VisibleToMembers)
            .FirstOrDefaultAsync(item =>
                item.Id == rewardId &&
                item.HouseholdId == child.HouseholdId &&
                item.IsActive &&
                item.VisibleToMembers.Any(visibility =>
                    visibility.HouseholdMemberId == child.Id));
        if (reward is null)
        {
            return NotFound();
        }

        if (await dbContext.RewardRedemptions.AnyAsync(redemption =>
                redemption.HouseholdId == child.HouseholdId &&
                redemption.HouseholdMemberId == child.Id &&
                redemption.RewardId == reward.Id &&
                redemption.Status == RewardRedemptionStatus.Pending))
        {
            TempData["StatusMessage"] = localizer["RewardAlreadyRequested"].Value;
            return RedirectToAction(nameof(Child));
        }

        var currentBalance = await pointBalanceService.GetBalanceAsync(
            child.HouseholdId, child.UserId);
        if (currentBalance < reward.PointCost)
        {
            TempData["StatusMessage"] = localizer["NotEnoughPointsForReward"].Value;
            return RedirectToAction(nameof(Child));
        }

        dbContext.RewardRedemptions.Add(
            rewardService.CreateRequest(reward, child, currentBalance));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["StatusMessage"] = localizer["RewardRequested", reward.Name].Value;
        return RedirectToAction(nameof(Child));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> ReviewRewardRequest(
        int id,
        RewardRedemptionStatus decision)
    {
        if (decision is not (
            RewardRedemptionStatus.Approved or RewardRedemptionStatus.Rejected))
        {
            return BadRequest();
        }

        var parent = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (parent is null)
        {
            return Forbid();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var redemption = await dbContext.RewardRedemptions
            .Include(item => item.HouseholdMember)
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.HouseholdId == parent.HouseholdId &&
                item.HouseholdMember.HouseholdId == parent.HouseholdId &&
                item.Status == RewardRedemptionStatus.Pending);
        if (redemption is null)
        {
            return NotFound();
        }

        var currentBalance = await pointBalanceService.GetBalanceAsync(
            parent.HouseholdId, redemption.HouseholdMember.UserId);
        if (decision == RewardRedemptionStatus.Approved &&
            currentBalance < redemption.PointsCost)
        {
            TempData["StatusMessage"] = localizer["NotEnoughPointsForApproval"].Value;
            return RedirectToAction(nameof(Parent));
        }

        rewardService.Review(redemption, parent, decision, currentBalance);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["StatusMessage"] = localizer[
            decision == RewardRedemptionStatus.Approved
                ? "RewardApproved"
                : "RewardRejected"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> PurchaseRewardForChild(
        [Bind(Prefix = "DirectRewardPurchase")] DirectRewardPurchaseInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = localizer["CheckRewardPurchaseFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var parent = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (parent is null)
        {
            return Forbid();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var reward = await dbContext.Rewards
            .Include(item => item.VisibleToMembers)
            .FirstOrDefaultAsync(item =>
                item.Id == input.RewardId &&
                item.HouseholdId == parent.HouseholdId &&
                item.IsActive);
        var child = await dbContext.HouseholdMembers.FirstOrDefaultAsync(member =>
            member.Id == input.HouseholdMemberId &&
            member.HouseholdId == parent.HouseholdId &&
            member.MemberRole == HouseholdMemberRole.Child &&
            member.IsActive);
        if (reward is null || child is null)
        {
            return NotFound();
        }

        var currentBalance = await pointBalanceService.GetBalanceAsync(
            parent.HouseholdId, child.UserId);
        if (currentBalance < reward.PointCost)
        {
            TempData["StatusMessage"] = localizer["NotEnoughPointsForPurchase"].Value;
            return RedirectToAction(nameof(Parent));
        }

        dbContext.RewardRedemptions.Add(
            rewardService.CreateDirectPurchase(reward, child, parent, currentBalance));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["StatusMessage"] = localizer[
            "RewardPurchasedForChild", reward.Name].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> UpdateMember(
        [Bind(Prefix = "UpdateMember")] UpdateMemberInput input)
    {
        if (!ModelState.IsValid ||
            input.Role is not (HouseholdMemberRole.Parent or HouseholdMemberRole.Child))
        {
            TempData["StatusMessage"] = localizer["CheckFormFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var currentUserId = userManager.GetUserId(User);
        var parentMembership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (parentMembership is null)
        {
            return Forbid();
        }

        var member = await dbContext.HouseholdMembers
            .Include(item => item.User)
            .FirstOrDefaultAsync(item =>
                item.Id == input.Id &&
                item.HouseholdId == parentMembership.HouseholdId &&
                item.IsActive);
        if (member is null)
        {
            return NotFound();
        }

        if (member.UserId == currentUserId && input.Role != HouseholdMemberRole.Parent)
        {
            TempData["StatusMessage"] = localizer["CannotChangeOwnParentRole"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var previousPhotoPath = member.User.ProfilePhotoPath;
        string? replacementPhotoPath = null;
        var photoChanged = input.RemovePhoto;
        if (input.Photo is not null)
        {
            var photoResult = await SaveUploadedImageAsync(
                input.Photo, "members", "InvalidMemberImage", "MemberImageTooLarge");
            if (!photoResult.Success)
            {
                TempData["StatusMessage"] = localizer[photoResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Parent));
            }

            replacementPhotoPath = photoResult.Path;
            photoChanged = true;
        }

        var previousRole = member.MemberRole;
        member.User.DisplayName = input.DisplayName.Trim();
        if (photoChanged)
        {
            member.User.ProfilePhotoPath = replacementPhotoPath;
        }
        member.MemberRole = input.Role;
        if (previousRole != input.Role)
        {
            var oldRole = previousRole == HouseholdMemberRole.Parent ? "Parent" : "Child";
            var newRole = input.Role == HouseholdMemberRole.Parent ? "Parent" : "Child";
            var addResult = await userManager.AddToRoleAsync(member.User, newRole);
            if (!addResult.Succeeded)
            {
                DeleteUploadedImage(replacementPhotoPath, "members");
                TempData["StatusMessage"] = localizer["AccountUpdateFailed"].Value;
                return RedirectToAction(nameof(Parent));
            }

            var removeResult = await userManager.RemoveFromRoleAsync(member.User, oldRole);
            if (!removeResult.Succeeded)
            {
                await userManager.RemoveFromRoleAsync(member.User, newRole);
                DeleteUploadedImage(replacementPhotoPath, "members");
                TempData["StatusMessage"] = localizer["AccountUpdateFailed"].Value;
                return RedirectToAction(nameof(Parent));
            }
        }

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            DeleteUploadedImage(replacementPhotoPath, "members");
            throw;
        }

        if (photoChanged && previousPhotoPath is not null)
        {
            DeleteUploadedImage(previousPhotoPath, "members");
        }
        TempData["StatusMessage"] = localizer["FamilyMemberUpdated"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> DeleteMember(int id)
    {
        var currentUserId = userManager.GetUserId(User);
        var parentMembership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (parentMembership is null)
        {
            return Forbid();
        }

        var member = await dbContext.HouseholdMembers.FirstOrDefaultAsync(item =>
            item.Id == id &&
            item.HouseholdId == parentMembership.HouseholdId &&
            item.IsActive);
        if (member is null)
        {
            return NotFound();
        }

        if (member.UserId == currentUserId)
        {
            TempData["StatusMessage"] = localizer["CannotDeleteYourself"].Value;
            return RedirectToAction(nameof(Parent));
        }

        member.IsActive = false;
        await dbContext.CareTasks
            .Where(task => task.AssignedMemberId == member.Id && task.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(task => task.IsActive, false));
        await dbContext.SaveChangesAsync();
        TempData["StatusMessage"] = localizer["FamilyMemberDeleted"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> UpdatePet(
        [Bind(Prefix = "UpdatePet")] UpdatePetInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = localizer["CheckFormFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var pet = await dbContext.Pets.FirstOrDefaultAsync(item =>
            item.Id == input.Id &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        if (pet is null)
        {
            return NotFound();
        }

        var oldPhotoPath = pet.PhotoPath;
        if (input.Photo is not null)
        {
            var photoResult = await SavePetPhotoAsync(input.Photo);
            if (!photoResult.Success)
            {
                TempData["StatusMessage"] = localizer[photoResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Parent));
            }

            pet.PhotoPath = photoResult.Path;
        }

        pet.Name = input.Name.Trim();
        pet.Species = input.Species.Trim();
        pet.BirthDate = input.BirthDate;
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            if (input.Photo is not null && pet.PhotoPath is not null)
            {
                DeletePetPhoto(pet.PhotoPath);
                pet.PhotoPath = oldPhotoPath;
            }

            throw;
        }
        if (input.Photo is not null && oldPhotoPath is not null)
        {
            DeletePetPhoto(oldPhotoPath);
        }

        TempData["StatusMessage"] = localizer["PetUpdated"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> DeletePet(int id)
    {
        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var pet = await dbContext.Pets.FirstOrDefaultAsync(item =>
            item.Id == id &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        if (pet is null)
        {
            return NotFound();
        }

        pet.IsActive = false;
        await dbContext.CareTasks
            .Where(task => task.PetId == pet.Id && task.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(task => task.IsActive, false));
        await dbContext.SaveChangesAsync();
        TempData["StatusMessage"] = localizer["PetDeleted"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> UpdateCareTask(
        [Bind(Prefix = "UpdateCareTask")] UpdateCareTaskInput input)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(input.Frequency))
        {
            TempData["StatusMessage"] = localizer["CheckFormFields"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var task = await dbContext.CareTasks.FirstOrDefaultAsync(item =>
            item.Id == input.Id &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        var petExists = await dbContext.Pets.AnyAsync(item =>
            item.Id == input.PetId &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        var memberExists = await dbContext.HouseholdMembers.AnyAsync(item =>
            item.Id == input.AssignedMemberId &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        if (task is null || !petExists || !memberExists)
        {
            return NotFound();
        }

        var category = await ResolveCategoryAsync(
            membership.HouseholdId, input.CategoryId, input.NewCategoryName);
        if (category is null)
        {
            TempData["StatusMessage"] = localizer["ChooseOrAddCategory"].Value;
            return RedirectToAction(nameof(Parent));
        }

        task.PetId = input.PetId;
        task.AssignedMemberId = input.AssignedMemberId;
        task.CareCategory = category;
        task.Frequency = input.Frequency;
        task.PointValue = input.PointValue;
        var oldImagePath = task.ImagePath;
        string? newImagePath = null;
        if (input.Image is not null)
        {
            var imageResult = await SaveUploadedImageAsync(
                input.Image, "tasks", "InvalidTaskImage", "TaskImageTooLarge");
            if (!imageResult.Success)
            {
                TempData["StatusMessage"] = localizer[imageResult.ErrorKey!].Value;
                return RedirectToAction(nameof(Parent));
            }

            newImagePath = imageResult.Path;
            task.ImagePath = newImagePath;
        }
        else if (input.RemoveImage)
        {
            task.ImagePath = null;
        }

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            DeleteUploadedImage(newImagePath, "tasks");
            throw;
        }

        if ((input.Image is not null || input.RemoveImage) && oldImagePath is not null)
        {
            DeleteUploadedImage(oldImagePath, "tasks");
        }
        RememberTaskSelections(task.PetId, task.AssignedMemberId);
        TempData["StatusMessage"] = localizer["CareTaskUpdated"].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> DeleteCareTask(int id)
    {
        var membership = await GetCurrentMembershipAsync(HouseholdMemberRole.Parent);
        if (membership is null)
        {
            return Forbid();
        }

        var task = await dbContext.CareTasks.FirstOrDefaultAsync(item =>
            item.Id == id &&
            item.HouseholdId == membership.HouseholdId &&
            item.IsActive);
        if (task is null)
        {
            return NotFound();
        }

        task.IsActive = false;
        await dbContext.SaveChangesAsync();
        TempData["StatusMessage"] = localizer["CareTaskDeleted"].Value;
        return RedirectToAction(nameof(Parent));
    }

    private async Task<HouseholdMember?> GetCurrentMembershipAsync(HouseholdMemberRole role)
    {
        var userId = userManager.GetUserId(User);
        return await dbContext.HouseholdMembers.FirstOrDefaultAsync(member =>
            member.UserId == userId &&
            member.MemberRole == role &&
            member.IsActive);
    }

    private async Task<HouseholdMember> GetOrCreateParentMembershipAsync(ApplicationUser user)
    {
        var membership = await dbContext.HouseholdMembers
            .Include(member => member.Household)
            .FirstOrDefaultAsync(member =>
                member.UserId == user.Id &&
                member.MemberRole == HouseholdMemberRole.Parent &&
                member.IsActive);

        if (membership is not null)
        {
            return membership;
        }

        var household = new Household
        {
            Name = localizer["DefaultHouseholdName"].Value
        };
        membership = new HouseholdMember
        {
            Household = household,
            UserId = user.Id,
            MemberRole = HouseholdMemberRole.Parent
        };
        dbContext.HouseholdMembers.Add(membership);
        await dbContext.SaveChangesAsync();
        return membership;
    }

    private string GetHouseholdDisplayName(Household household) =>
        household.IsNameCustomized
            ? household.Name
            : localizer["DefaultHouseholdName"].Value;

    private string GetCategoryName(CareCategory category) =>
        category.Code is not null
            ? localizer[$"Category_{category.Code}"].Value
            : category.CustomName ?? string.Empty;

    private string GetTaskImagePath(CareTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.ImagePath))
        {
            return task.ImagePath;
        }

        var categoryCode = task.CareCategory?.Code;
        if (categoryCode is not null &&
            DefaultTaskImages.TryGetValue(categoryCode, out var defaultPath))
        {
            return defaultPath;
        }

        return task.Pet?.PhotoPath ?? "/images/tasks/playing.webp";
    }

    private string GetCareLogImagePath(CareLog log) =>
        log.Photos.OrderBy(photo => photo.CreatedAt).FirstOrDefault()?.ImagePath
        ?? GetTaskImagePath(log.CareTask);

    private async Task<CareCategory?> ResolveCategoryAsync(
        int householdId,
        int? categoryId,
        string? newCategoryName)
    {
        var trimmedName = newCategoryName?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedName))
        {
            var existing = await dbContext.CareCategories.FirstOrDefaultAsync(item =>
                item.HouseholdId == householdId &&
                item.CustomName == trimmedName &&
                item.IsActive);
            if (existing is not null)
            {
                return existing;
            }

            var category = new CareCategory
            {
                HouseholdId = householdId,
                CustomName = trimmedName
            };
            dbContext.CareCategories.Add(category);
            return category;
        }

        return categoryId is null
            ? null
            : await dbContext.CareCategories.FirstOrDefaultAsync(item =>
                item.Id == categoryId &&
                item.IsActive &&
                (item.HouseholdId == null || item.HouseholdId == householdId));
    }

    private async Task<(bool Success, string? Path, string? ErrorKey)> SavePetPhotoAsync(
        IFormFile photo)
        => await SaveUploadedImageAsync(
            photo, "pets", "InvalidPetImage", "PetImageTooLarge");

    private async Task<(bool Success, string? Path, string? ErrorKey)> SaveUploadedImageAsync(
        IFormFile photo,
        string folder,
        string invalidImageKey,
        string tooLargeKey)
    {
        var extension = Path.GetExtension(photo.FileName);
        if (photo.Length == 0 ||
            !AllowedPhotoExtensions.Contains(extension) ||
            !AllowedPhotoContentTypes.Contains(photo.ContentType) ||
            !await HasValidImageSignatureAsync(photo, extension))
        {
            return (false, null, invalidImageKey);
        }

        if (photo.Length > MaximumPetPhotoBytes)
        {
            return (false, null, tooLargeKey);
        }

        var uploadDirectory = Path.Combine(environment.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(uploadDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(uploadDirectory, fileName);
        await using var stream = System.IO.File.Create(path);
        await photo.CopyToAsync(stream);
        return (true, $"/uploads/{folder}/{fileName}", null);
    }

    private static async Task<bool> HasValidImageSignatureAsync(IFormFile photo, string extension)
    {
        var header = new byte[12];
        await using var stream = photo.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return bytesRead >= 3 &&
                   header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            return bytesRead >= pngSignature.Length &&
                   header.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature);
        }

        return bytesRead >= 12 &&
               header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
               header.AsSpan(8, 4).SequenceEqual("WEBP"u8);
    }

    private void DeletePetPhoto(string photoPath)
        => DeleteUploadedImage(photoPath, "pets");

    private void DeleteUploadedImage(string? photoPath, string folder)
    {
        if (string.IsNullOrWhiteSpace(photoPath))
        {
            return;
        }

        var relativePath = photoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(environment.WebRootPath, relativePath));
        var uploadRoot = Path.GetFullPath(
            Path.Combine(environment.WebRootPath, "uploads", folder)) +
            Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase) &&
            System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

    private int GetRememberedSelection(string key, IEnumerable<int> allowedIds)
    {
        var allowed = allowedIds.ToHashSet();
        var remembered = HttpContext.Session.GetInt32(key);
        return remembered is not null && allowed.Contains(remembered.Value)
            ? remembered.Value
            : allowed.FirstOrDefault();
    }

    private void RememberTaskSelections(int petId, int memberId)
    {
        HttpContext.Session.SetInt32("LastTaskPetId", petId);
        HttpContext.Session.SetInt32("LastTaskMemberId", memberId);
    }
}

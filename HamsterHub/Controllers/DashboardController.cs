using HamsterHub.Data;
using HamsterHub.Models;
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
    IWebHostEnvironment environment) : Controller
{
    private const long MaximumPetPhotoBytes = 5 * 1024 * 1024;
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
            .Include(log => log.CompletedByUser)
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Where(log => log.Pet.HouseholdId == householdId && log.Status == CareLogStatus.Pending)
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

        var model = new ParentDashboardViewModel
        {
            ParentName = user.DisplayName,
            HouseholdName = membership.Household.Name,
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
                    member.UserId == user.Id))
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
                    task.PointValue))
                .ToList(),
            PendingCare = pendingEntities
                .Select(log => new PendingCareSummary(
                    log.Id,
                    log.CompletedByUser.DisplayName,
                    log.Pet.Name,
                    GetCategoryName(log.CareTask.CareCategory),
                    log.PointsAwarded,
                    log.CompletedAt))
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
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Where(log => log.CompletedByUserId == user.Id)
            .OrderByDescending(log => log.CompletedAt)
            .Take(8)
            .ToListAsync();
        var familyActivityEntities = new List<CareLog>();
        if (membership.CanViewOtherChildrenHistory)
        {
            var visibleUserIds = await dbContext.HouseholdMembers
                .AsNoTracking()
                .Where(member =>
                    member.HouseholdId == householdId &&
                    member.Id != membership.Id &&
                    member.MemberRole == HouseholdMemberRole.Child &&
                    member.IsActive &&
                    member.ShareHistoryWithChildren)
                .Select(member => member.UserId)
                .ToListAsync();
            familyActivityEntities = await dbContext.CareLogs
                .AsNoTracking()
                .Include(log => log.CompletedByUser)
                .Include(log => log.Pet)
                .Include(log => log.CareTask)
                    .ThenInclude(task => task.CareCategory)
                .Where(log =>
                    visibleUserIds.Contains(log.CompletedByUserId) &&
                    log.Status == CareLogStatus.Approved &&
                    log.PointsTotalAfterApproval != null)
                .OrderByDescending(log => log.ApprovedAt)
                .Take(10)
                .ToListAsync();
        }

        return View(new KidDashboardViewModel
        {
            ChildName = user.DisplayName,
            HouseholdName = membership.Household.Name,
            ApprovedPoints = await dbContext.CareLogs
                .Where(log =>
                    log.CompletedByUserId == user.Id &&
                    log.Status == CareLogStatus.Approved)
                .SumAsync(log => (int?)log.PointsAwarded) ?? 0,
            PendingCount = await dbContext.CareLogs.CountAsync(log =>
                log.CompletedByUserId == user.Id &&
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
                    task.PointValue))
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
                    log.ApprovedAt))
                .ToList(),
            CanViewFamilyActivity = membership.CanViewOtherChildrenHistory,
            FamilyActivity = familyActivityEntities
                .Select(log => new FamilyActivitySummary(
                    log.CompletedByUser.DisplayName,
                    log.Pet.Name,
                    log.Pet.PhotoPath,
                    GetCategoryName(log.CareTask.CareCategory),
                    log.PointsAwarded,
                    log.PointsTotalAfterApproval!.Value,
                    log.CompletedAt,
                    log.ApprovedAt!.Value))
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
            .Include(log => log.Pet)
            .Include(log => log.CareTask)
                .ThenInclude(task => task.CareCategory)
            .Include(log => log.ApprovedByUser)
            .Where(log =>
                log.CompletedByUserId == member.UserId &&
                log.Pet.HouseholdId == membership.HouseholdId)
            .OrderByDescending(log => log.CompletedAt)
            .ToListAsync();

        return View(new FamilyMemberHistoryViewModel
        {
            MemberId = member.Id,
            DisplayName = member.User.DisplayName,
            Email = member.User.Email ?? string.Empty,
            Role = member.MemberRole,
            CurrentPoints = logs
                .Where(log => log.Status == CareLogStatus.Approved)
                .Sum(log => log.PointsAwarded),
            CanViewOtherChildrenHistory = member.CanViewOtherChildrenHistory,
            ShareHistoryWithChildren = member.ShareHistoryWithChildren,
            History = logs.Select(log => new MemberCareHistorySummary(
                log.Pet.Name,
                log.Pet.PhotoPath,
                GetCategoryName(log.CareTask.CareCategory),
                log.PointsAwarded,
                log.PointsTotalAfterApproval,
                log.Status,
                log.CompletedAt,
                log.ApprovedAt,
                log.ApprovedByUser?.DisplayName)).ToList()
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

        var newUser = new ApplicationUser
        {
            DisplayName = input.DisplayName.Trim(),
            Email = email,
            UserName = email
        };
        var createResult = await userManager.CreateAsync(newUser, input.Password);
        if (!createResult.Succeeded)
        {
            TempData["StatusMessage"] = localizer["PasswordRequirements"].Value;
            return RedirectToAction(nameof(Parent));
        }

        var roleName = input.Role == HouseholdMemberRole.Parent ? "Parent" : "Child";
        var roleResult = await userManager.AddToRoleAsync(newUser, roleName);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(newUser);
            TempData["StatusMessage"] = localizer["AccountCreationFailed"].Value;
            return RedirectToAction(nameof(Parent));
        }

        dbContext.HouseholdMembers.Add(new HouseholdMember
        {
            HouseholdId = membership.HouseholdId,
            UserId = newUser.Id,
            MemberRole = input.Role
        });
        await dbContext.SaveChangesAsync();

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

        dbContext.CareTasks.Add(new CareTask
        {
            HouseholdId = membership.HouseholdId,
            PetId = pet.Id,
            AssignedMemberId = assignedMember.Id,
            CareCategory = category,
            Frequency = input.Frequency,
            PointValue = input.PointValue
        });
        await dbContext.SaveChangesAsync();
        RememberTaskSelections(pet.Id, assignedMember.Id);

        TempData["StatusMessage"] = localizer["CareTaskAdded", GetCategoryName(category)].Value;
        return RedirectToAction(nameof(Parent));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Child")]
    public async Task<IActionResult> CompleteTask(int careTaskId)
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

        if (careTask is null)
        {
            return Forbid();
        }

        var earliestAllowed = careTask.Frequency switch
        {
            CareTaskFrequency.Daily =>
                new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero),
            CareTaskFrequency.Weekly => DateTimeOffset.UtcNow.AddDays(-7),
            _ => (DateTimeOffset?)null
        };

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

        dbContext.CareLogs.Add(new CareLog
        {
            PetId = careTask.PetId,
            CareTaskId = careTask.Id,
            CompletedByUserId = user.Id,
            PointsAwarded = careTask.PointValue
        });
        await dbContext.SaveChangesAsync();

        TempData["StatusMessage"] = localizer["TaskSentForApproval"].Value;
        return RedirectToAction(nameof(Child));
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
            .FirstOrDefaultAsync(log =>
                log.Id == id &&
                log.Pet.HouseholdId == membership.HouseholdId &&
                log.Status == CareLogStatus.Pending);

        if (careLog is null)
        {
            return NotFound();
        }

        careLog.Status = decision;
        careLog.ApprovedByUserId = parent.Id;
        careLog.ApprovedAt = DateTimeOffset.UtcNow;
        careLog.PointsTotalAfterApproval = decision == CareLogStatus.Approved
            ? (await dbContext.CareLogs
                .Where(log =>
                    log.CompletedByUserId == careLog.CompletedByUserId &&
                    log.Status == CareLogStatus.Approved)
                .SumAsync(log => (int?)log.PointsAwarded) ?? 0) + careLog.PointsAwarded
            : null;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["StatusMessage"] = localizer[
            decision == CareLogStatus.Approved ? "CareApproved" : "CareRejected"].Value;
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

        var previousRole = member.MemberRole;
        member.User.DisplayName = input.DisplayName.Trim();
        member.MemberRole = input.Role;
        if (previousRole != input.Role)
        {
            var oldRole = previousRole == HouseholdMemberRole.Parent ? "Parent" : "Child";
            var newRole = input.Role == HouseholdMemberRole.Parent ? "Parent" : "Child";
            var addResult = await userManager.AddToRoleAsync(member.User, newRole);
            if (!addResult.Succeeded)
            {
                TempData["StatusMessage"] = localizer["AccountUpdateFailed"].Value;
                return RedirectToAction(nameof(Parent));
            }

            var removeResult = await userManager.RemoveFromRoleAsync(member.User, oldRole);
            if (!removeResult.Succeeded)
            {
                await userManager.RemoveFromRoleAsync(member.User, newRole);
                TempData["StatusMessage"] = localizer["AccountUpdateFailed"].Value;
                return RedirectToAction(nameof(Parent));
            }
        }

        await dbContext.SaveChangesAsync();
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
        await dbContext.SaveChangesAsync();
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
            Name = localizer["DefaultHouseholdName", user.DisplayName]
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

    private string GetCategoryName(CareCategory category) =>
        category.Code is not null
            ? localizer[$"Category_{category.Code}"].Value
            : category.CustomName ?? string.Empty;

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
    {
        var extension = Path.GetExtension(photo.FileName);
        if (photo.Length == 0 ||
            !AllowedPhotoExtensions.Contains(extension) ||
            !AllowedPhotoContentTypes.Contains(photo.ContentType))
        {
            return (false, null, "InvalidPetImage");
        }

        if (photo.Length > MaximumPetPhotoBytes)
        {
            return (false, null, "PetImageTooLarge");
        }

        var uploadDirectory = Path.Combine(environment.WebRootPath, "uploads", "pets");
        Directory.CreateDirectory(uploadDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(uploadDirectory, fileName);
        await using var stream = System.IO.File.Create(path);
        await photo.CopyToAsync(stream);
        return (true, $"/uploads/pets/{fileName}", null);
    }

    private void DeletePetPhoto(string photoPath)
    {
        var relativePath = photoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(environment.WebRootPath, relativePath));
        var uploadRoot = Path.GetFullPath(
            Path.Combine(environment.WebRootPath, "uploads", "pets")) +
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

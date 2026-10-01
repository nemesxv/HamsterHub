using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace HamsterHub.Models;

public class ParentDashboardViewModel
{
    public string ParentName { get; set; } = string.Empty;
    public string HouseholdName { get; set; } = string.Empty;
    public IReadOnlyList<FamilyMemberSummary> Members { get; set; } = [];
    public IReadOnlyList<PetSummary> Pets { get; set; } = [];
    public IReadOnlyList<CareCategoryOption> CareCategories { get; set; } = [];
    public IReadOnlyList<CareTaskSummary> CareTasks { get; set; } = [];
    public IReadOnlyList<PendingCareSummary> PendingCare { get; set; } = [];
    public IReadOnlyList<ParentRewardSummary> Rewards { get; set; } = [];
    public IReadOnlyList<PendingRewardSummary> PendingRewards { get; set; } = [];
    public IReadOnlyList<RewardChildOption> RewardChildren { get; set; } = [];
    public AddMemberInput AddMember { get; set; } = new();
    public AddPetInput AddPet { get; set; } = new();
    public AddCareTaskInput AddCareTask { get; set; } = new();
    public AddRewardInput AddReward { get; set; } = new();
    public DirectRewardPurchaseInput DirectRewardPurchase { get; set; } = new();
}

public class KidDashboardViewModel
{
    public string ChildName { get; set; } = string.Empty;
    public string HouseholdName { get; set; } = string.Empty;
    public int CurrentPoints { get; set; }
    public int PendingCount { get; set; }
    public IReadOnlyList<PetSummary> Pets { get; set; } = [];
    public IReadOnlyList<CareTaskSummary> CareTasks { get; set; } = [];
    public IReadOnlyList<KidCareHistorySummary> RecentCare { get; set; } = [];
    public IReadOnlyList<KidFamilyMemberSummary> FamilyMembers { get; set; } = [];
    public IReadOnlyList<KidRewardSummary> Rewards { get; set; } = [];
    public IReadOnlyList<KidRewardRedemptionSummary> RewardRedemptions { get; set; } = [];
}

public record FamilyMemberSummary(
    int Id,
    string UserId,
    string DisplayName,
    string Email,
    HouseholdMemberRole Role,
    bool IsCurrentUser,
    string? PhotoPath);
public record PetSummary(
    int Id,
    string Name,
    string Species,
    DateOnly? BirthDate,
    string? PhotoPath);
public record CareCategoryOption(int Id, string Name);
public record CareTaskSummary(
    int Id,
    int? PetId,
    string PetName,
    string? PetPhotoPath,
    int? CategoryId,
    string CategoryName,
    int AssignedMemberId,
    string AssignedMemberName,
    CareTaskFrequency Frequency,
    int PointValue,
    string ImagePath,
    bool HasCustomImage,
    bool CanCurrentUserComplete,
    CareLogStatus? CurrentPeriodStatus = null,
    HamsterHub.Contracts.TaskReminderDto? Reminder = null);
public record TaskReminderFormModel(string Prefix, string Id, HamsterHub.Contracts.TaskReminderDto? Reminder);
public record PendingCareSummary(
    int Id,
    string ChildName,
    string PetName,
    string TaskName,
    int Points,
    DateTimeOffset CompletedAt,
    IReadOnlyList<string> PhotoPaths);
public record KidCareHistorySummary(
    string PetName,
    string? PetPhotoPath,
    string TaskName,
    int Points,
    int? PointsTotalAfterApproval,
    CareLogStatus Status,
    DateTimeOffset CompletedAt,
    DateTimeOffset? ApprovedAt,
    string ImagePath,
    IReadOnlyList<string> PhotoPaths);
public record KidFamilyMemberSummary(
    int Id,
    string DisplayName,
    HouseholdMemberRole Role,
    string? PhotoPath,
    int CurrentPoints);
public record ParentRewardSummary(
    int Id,
    string Name,
    int PointCost,
    string? ImagePath,
    IReadOnlyList<string> VisibleToChildren);
public record PendingRewardSummary(
    int Id,
    string RewardName,
    string? ImagePath,
    string ChildName,
    int PointsCost,
    DateTimeOffset RequestedAt);
public record RewardChildOption(int Id, string DisplayName, int CurrentPoints);
public record KidRewardSummary(
    int Id,
    string Name,
    int PointCost,
    string? ImagePath,
    bool CanAfford,
    bool HasPendingRequest);
public record KidRewardRedemptionSummary(
    string RewardName,
    string? ImagePath,
    int PointsCost,
    RewardRedemptionStatus Status,
    DateTimeOffset RequestedAt,
    int? PointsBalanceAfterApproval);

public class FamilyMemberHistoryViewModel
{
    public int MemberId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoPath { get; set; }
    public HouseholdMemberRole Role { get; set; }
    public int CurrentPoints { get; set; }
    public bool CanViewOtherChildrenHistory { get; set; }
    public bool ShareHistoryWithChildren { get; set; }
    public IReadOnlyList<MemberPointHistorySummary> History { get; set; } = [];
}

public class KidFamilyMemberHistoryViewModel
{
    public int MemberId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public HouseholdMemberRole Role { get; set; }
    public string? PhotoPath { get; set; }
    public int CurrentPoints { get; set; }
    public IReadOnlyList<KidSharedCareHistorySummary> History { get; set; } = [];
}

public record KidSharedCareHistorySummary(
    string PetName,
    string TaskName,
    DateTimeOffset CompletedAt,
    string ImagePath,
    IReadOnlyList<string> PhotoPaths);

public record MemberPointHistorySummary(
    MemberPointHistoryKind Kind,
    string Title,
    string Subtitle,
    int PointsChange,
    int? PointsTotalAfter,
    string StatusLocalizationKey,
    string StatusCssClass,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ReviewedAt,
    string? ApprovedByName,
    string? ImagePath,
    IReadOnlyList<string> PhotoPaths);

public enum MemberPointHistoryKind { Care = 1, Reward = 2 }

public class AddMemberInput
{
    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "DisplayNameLength")]
    [Display(Name = "DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required"), EmailAddress(ErrorMessage = "InvalidEmail")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required")]
    [MinLength(4, ErrorMessage = "PasswordLength")]
    [DataType(DataType.Password)]
    [Display(Name = "TemporaryPassword")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "FamilyRole")]
    public HouseholdMemberRole Role { get; set; } = HouseholdMemberRole.Child;

    [Display(Name = "MemberPhoto")]
    public IFormFile? Photo { get; set; }
}

public class AddPetInput
{
    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 1)]
    [Display(Name = "PetName")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Species")]
    public string Species { get; set; } = string.Empty;

    [Display(Name = "BirthDate")]
    [DataType(DataType.Date)]
    public DateOnly? BirthDate { get; set; }

    [Display(Name = "PetPhoto")]
    public IFormFile? Photo { get; set; }
}

public class AddCareTaskInput
{
    [Display(Name = "TaskNotification")]
    public bool NotificationEnabled { get; set; }
    [Display(Name = "NotificationTime")]
    public TimeOnly? ReminderTime { get; set; }
    [Display(Name = "NotificationStartDate")]
    public DateOnly? ReminderStartDate { get; set; }
    [StringLength(100)]
    public string? ReminderTimeZoneId { get; set; }

    public bool HasValidReminder => !NotificationEnabled ||
        (ReminderTime.HasValue && ReminderStartDate.HasValue &&
         HamsterHub.Contracts.ReminderSchedule.IsValid(Frequency.ToString(), Reminder));
    public HamsterHub.Contracts.TaskReminderDto? Reminder => NotificationEnabled &&
        ReminderTime is { } time && ReminderStartDate is { } date && ReminderTimeZoneId is { } zone
            ? new(time, date, zone) : null;
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "TaskName")]
    public string? Name { get; set; }

    [Display(Name = "TaskPet")]
    public int? PetId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Required")]
    [Display(Name = "AssignedMember")]
    public int AssignedMemberId { get; set; }

    [Display(Name = "TaskCategory")]
    public int? CategoryId { get; set; }

    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "NewCategory")]
    public string? NewCategoryName { get; set; }

    [Display(Name = "TaskFrequency")]
    public CareTaskFrequency Frequency { get; set; } = CareTaskFrequency.Daily;

    [Range(0, 1000, ErrorMessage = "PointRange")]
    [Display(Name = "PointValue")]
    public int PointValue { get; set; } = 5;

    [Display(Name = "TaskImage")]
    public IFormFile? Image { get; set; }
}

public class AddRewardInput
{
    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "RewardNameLength")]
    [Display(Name = "RewardName")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 100000, ErrorMessage = "RewardPointRange")]
    [Display(Name = "RewardPointCost")]
    public int PointCost { get; set; } = 10;

    [Display(Name = "RewardImage")]
    public IFormFile? Image { get; set; }

    [Display(Name = "RewardVisibleTo")]
    [MinLength(1, ErrorMessage = "ChooseRewardAudience")]
    public List<int> VisibleToMemberIds { get; set; } = [];
}

public class DirectRewardPurchaseInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Required")]
    public int RewardId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Required")]
    public int HouseholdMemberId { get; set; }
}

public class UpdateMemberInput
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "DisplayNameLength")]
    [Display(Name = "DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "FamilyRole")]
    public HouseholdMemberRole Role { get; set; }

    [Display(Name = "MemberPhoto")]
    public IFormFile? Photo { get; set; }

    public bool RemovePhoto { get; set; }
}

public class UpdatePetInput : AddPetInput
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }
}

public class UpdateCareTaskInput : AddCareTaskInput
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    public bool RemoveImage { get; set; }
}

public class UpdateHistoryPrivacyInput
{
    [Range(1, int.MaxValue)]
    public int MemberId { get; set; }

    public bool CanViewOtherChildrenHistory { get; set; }
    public bool ShareHistoryWithChildren { get; set; }
}

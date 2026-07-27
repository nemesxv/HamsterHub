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
    public AddMemberInput AddMember { get; set; } = new();
    public AddPetInput AddPet { get; set; } = new();
    public AddCareTaskInput AddCareTask { get; set; } = new();
}

public class KidDashboardViewModel
{
    public string ChildName { get; set; } = string.Empty;
    public string HouseholdName { get; set; } = string.Empty;
    public int ApprovedPoints { get; set; }
    public int PendingCount { get; set; }
    public IReadOnlyList<PetSummary> Pets { get; set; } = [];
    public IReadOnlyList<CareTaskSummary> CareTasks { get; set; } = [];
    public IReadOnlyList<KidCareHistorySummary> RecentCare { get; set; } = [];
    public bool CanViewFamilyActivity { get; set; }
    public IReadOnlyList<FamilyActivitySummary> FamilyActivity { get; set; } = [];
}

public record FamilyMemberSummary(
    int Id,
    string UserId,
    string DisplayName,
    string Email,
    HouseholdMemberRole Role,
    bool IsCurrentUser);
public record PetSummary(
    int Id,
    string Name,
    string Species,
    DateOnly? BirthDate,
    string? PhotoPath);
public record CareCategoryOption(int Id, string Name);
public record CareTaskSummary(
    int Id,
    int PetId,
    string PetName,
    string? PetPhotoPath,
    int CategoryId,
    string CategoryName,
    int AssignedMemberId,
    string AssignedMemberName,
    CareTaskFrequency Frequency,
    int PointValue);
public record PendingCareSummary(
    int Id,
    string ChildName,
    string PetName,
    string TaskName,
    int Points,
    DateTimeOffset CompletedAt);
public record KidCareHistorySummary(
    string PetName,
    string? PetPhotoPath,
    string TaskName,
    int Points,
    int? PointsTotalAfterApproval,
    CareLogStatus Status,
    DateTimeOffset CompletedAt,
    DateTimeOffset? ApprovedAt);
public record FamilyActivitySummary(
    string ChildName,
    string PetName,
    string? PetPhotoPath,
    string TaskName,
    int Points,
    int PointsTotalAfterApproval,
    DateTimeOffset CompletedAt,
    DateTimeOffset ApprovedAt);

public class FamilyMemberHistoryViewModel
{
    public int MemberId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public HouseholdMemberRole Role { get; set; }
    public int CurrentPoints { get; set; }
    public bool CanViewOtherChildrenHistory { get; set; }
    public bool ShareHistoryWithChildren { get; set; }
    public IReadOnlyList<MemberCareHistorySummary> History { get; set; } = [];
}

public record MemberCareHistorySummary(
    string PetName,
    string? PetPhotoPath,
    string TaskName,
    int Points,
    int? PointsTotalAfterApproval,
    CareLogStatus Status,
    DateTimeOffset ReportedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedByName);

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
    [StringLength(100, MinimumLength = 6, ErrorMessage = "PasswordLength")]
    [DataType(DataType.Password)]
    [Display(Name = "TemporaryPassword")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "FamilyRole")]
    public HouseholdMemberRole Role { get; set; } = HouseholdMemberRole.Child;
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
    [Range(1, int.MaxValue, ErrorMessage = "Required")]
    [Display(Name = "TaskPet")]
    public int PetId { get; set; }

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
}

public class UpdateHistoryPrivacyInput
{
    [Range(1, int.MaxValue)]
    public int MemberId { get; set; }

    public bool CanViewOtherChildrenHistory { get; set; }
    public bool ShareHistoryWithChildren { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace HamsterHub.Contracts;

public sealed record CreatedItemDto(int Id);

public sealed record HouseholdHubDto(
    IReadOnlyList<HouseholdMemberItemDto> Members,
    IReadOnlyList<PetItemDto> Pets,
    IReadOnlyList<CategoryItemDto> Categories,
    IReadOnlyList<ManagedTaskItemDto> Tasks,
    IReadOnlyList<RewardItemDto> Rewards,
    IReadOnlyList<RewardRequestItemDto> RewardRequests,
    IReadOnlyList<RewardHistoryItemDto> RewardHistory);

public sealed record HouseholdMemberItemDto(int Id, string DisplayName, string Email, string Role,
    bool IsCurrentUser, string? PhotoPath, int Balance);
public sealed record PetItemDto(int Id, string Name, string Species, DateOnly? BirthDate, string? PhotoPath);
public sealed record CategoryItemDto(int Id, string Name);
public sealed record ManagedTaskItemDto(int Id, int? PetId, string PetName, int? CategoryId,
    string Name, int AssignedMemberId, string AssignedMemberName, string Frequency, int Points,
    string ImagePath, bool CanComplete, TaskReminderDto? Reminder = null);
public sealed record RewardItemDto(int Id, string Name, int PointCost, string? ImagePath,
    IReadOnlyList<int> VisibleToMemberIds, bool CanAfford, bool HasPendingRequest);
public sealed record RewardRequestItemDto(int Id, string RewardName, string ChildName,
    int PointsCost, DateTimeOffset RequestedAt, string? ImagePath);
public sealed record RewardHistoryItemDto(int Id, string RewardName, int PointsCost, string Status,
    DateTimeOffset RequestedAt, int? BalanceAfterApproval, string? ImagePath);

public sealed record CreateMemberRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, EmailAddress] string Email,
    [Required, StringLength(100, MinimumLength = 6)] string Password,
    [Required] string Role);
public sealed record CreatePetRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, StringLength(100, MinimumLength = 2)] string Species,
    DateOnly? BirthDate);
public sealed record UpdateMemberRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required] string Role);
public sealed record UpdatePetRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, StringLength(100, MinimumLength = 2)] string Species,
    DateOnly? BirthDate);
public sealed record CreateTaskRequest(int? PetId, int AssignedMemberId, int? CategoryId,
    [StringLength(100, MinimumLength = 2)] string? NewCategoryName,
    [Required] string Frequency, [Range(0, 1000)] int Points,
    [StringLength(100, MinimumLength = 2)] string? Name = null, TaskReminderDto? Reminder = null);
public sealed record UpdateTaskRequest(int? PetId, int AssignedMemberId, int? CategoryId,
    [StringLength(100, MinimumLength = 2)] string? NewCategoryName,
    [Required] string Frequency, [Range(0, 1000)] int Points,
    [StringLength(100, MinimumLength = 2)] string? Name = null, TaskReminderDto? Reminder = null);
public sealed record CreateRewardRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Range(1, 100000)] int PointCost,
    [MinLength(1)] IReadOnlyList<int> VisibleToMemberIds);
public sealed record RewardDecisionRequest(bool Approve);
public sealed record DirectRewardRequest(int RewardId, int ChildMemberId);

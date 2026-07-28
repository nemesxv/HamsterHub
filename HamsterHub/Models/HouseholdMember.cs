namespace HamsterHub.Models;

public class HouseholdMember
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public HouseholdMemberRole MemberRole { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool CanViewOtherChildrenHistory { get; set; }
    public bool ShareHistoryWithChildren { get; set; }
    public Household Household { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public ICollection<CareTask> AssignedCareTasks { get; set; } = [];
    public ICollection<RewardVisibility> VisibleRewards { get; set; } = [];
    public ICollection<RewardRedemption> RewardRedemptions { get; set; } = [];
}

public enum HouseholdMemberRole { Parent = 1, Child = 2 }

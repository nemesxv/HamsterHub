namespace HamsterHub.Models;

public class RewardRedemption
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int RewardId { get; set; }
    public int HouseholdMemberId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RewardName { get; set; } = string.Empty;
    public string? RewardImagePath { get; set; }
    public int PointsCost { get; set; }
    public RewardRedemptionStatus Status { get; set; } = RewardRedemptionStatus.Pending;
    public string? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public int? PointsBalanceAfterApproval { get; set; }
    public Household Household { get; set; } = null!;
    public Reward Reward { get; set; } = null!;
    public HouseholdMember HouseholdMember { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
}

public enum RewardRedemptionStatus { Pending = 1, Approved = 2, Rejected = 3 }

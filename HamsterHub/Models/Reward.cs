namespace HamsterHub.Models;

public class Reward
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PointCost { get; set; }
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Household Household { get; set; } = null!;
    public ICollection<RewardVisibility> VisibleToMembers { get; set; } = [];
    public ICollection<RewardRedemption> Redemptions { get; set; } = [];
}

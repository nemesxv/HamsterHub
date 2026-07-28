namespace HamsterHub.Models;

public class RewardVisibility
{
    public int RewardId { get; set; }
    public int HouseholdMemberId { get; set; }
    public Reward Reward { get; set; } = null!;
    public HouseholdMember HouseholdMember { get; set; } = null!;
}

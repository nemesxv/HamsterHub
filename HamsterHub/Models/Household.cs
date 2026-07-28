namespace HamsterHub.Models;

public class Household
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsNameCustomized { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HouseholdMember> Members { get; set; } = [];
    public ICollection<Pet> Pets { get; set; } = [];
    public ICollection<CareCategory> CareCategories { get; set; } = [];
    public ICollection<CareTask> CareTasks { get; set; } = [];
    public ICollection<Reward> Rewards { get; set; } = [];
    public ICollection<RewardRedemption> RewardRedemptions { get; set; } = [];
}

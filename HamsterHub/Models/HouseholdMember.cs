namespace HamsterHub.Models;

public class HouseholdMember
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public HouseholdMemberRole MemberRole { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public Household Household { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}

public enum HouseholdMemberRole { Parent = 1, Child = 2 }

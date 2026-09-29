namespace HamsterHub.Models;

public class CareTask
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int HouseholdId { get; set; }
    public int? PetId { get; set; }
    public int? CareCategoryId { get; set; }
    public int AssignedMemberId { get; set; }
    public int PointValue { get; set; }
    public CareTaskFrequency Frequency { get; set; }
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; } = true;
    public Household Household { get; set; } = null!;
    public Pet? Pet { get; set; }
    public CareCategory? CareCategory { get; set; }
    public HouseholdMember AssignedMember { get; set; } = null!;
    public ICollection<CareLog> CareLogs { get; set; } = [];
}

public enum CareTaskFrequency { Daily = 1, Weekly = 2, AsNeeded = 3 }

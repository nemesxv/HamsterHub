namespace HamsterHub.Models;

public class CareTask
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CareTaskCategory Category { get; set; }
    public int PointValue { get; set; }
    public CareTaskFrequency Frequency { get; set; }
    public bool IsActive { get; set; } = true;
    public Household Household { get; set; } = null!;
    public ICollection<CareLog> CareLogs { get; set; } = [];
}

public enum CareTaskCategory { Feeding = 1, Cleaning = 2, Playing = 3, Health = 4, Other = 5 }
public enum CareTaskFrequency { Daily = 1, Weekly = 2, AsNeeded = 3 }

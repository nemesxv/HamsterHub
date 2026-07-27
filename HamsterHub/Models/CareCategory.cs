namespace HamsterHub.Models;

public class CareCategory
{
    public int Id { get; set; }
    public int? HouseholdId { get; set; }
    public string? Code { get; set; }
    public string? CustomName { get; set; }
    public bool IsActive { get; set; } = true;
    public Household? Household { get; set; }
    public ICollection<CareTask> CareTasks { get; set; } = [];
}

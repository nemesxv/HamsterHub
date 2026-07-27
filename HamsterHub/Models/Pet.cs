namespace HamsterHub.Models;

public class Pet
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public DateOnly? BirthDate { get; set; }
    public string? PhotoPath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Household Household { get; set; } = null!;
    public ICollection<CareTask> CareTasks { get; set; } = [];
    public ICollection<CareLog> CareLogs { get; set; } = [];
}

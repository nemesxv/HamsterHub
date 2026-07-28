namespace HamsterHub.Models;

public class CareLogPhoto
{
    public int Id { get; set; }
    public int CareLogId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public CareLog CareLog { get; set; } = null!;
}

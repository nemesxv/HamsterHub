namespace HamsterHub.Models;

public class CareLog
{
    public int Id { get; set; }
    public int? PetId { get; set; }
    public int CareTaskId { get; set; }
    public string CompletedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public int PointsAwarded { get; set; }
    public CareLogStatus Status { get; set; } = CareLogStatus.Pending;
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public int? PointsTotalAfterApproval { get; set; }
    public Pet? Pet { get; set; }
    public CareTask CareTask { get; set; } = null!;
    public ApplicationUser CompletedByUser { get; set; } = null!;
    public ApplicationUser? ApprovedByUser { get; set; }
    public ICollection<CareLogPhoto> Photos { get; set; } = [];
}

public enum CareLogStatus { Pending = 1, Approved = 2, Rejected = 3 }

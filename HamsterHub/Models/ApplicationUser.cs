using Microsoft.AspNetCore.Identity;

namespace HamsterHub.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfilePhotoPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public ICollection<HouseholdMember> HouseholdMemberships { get; set; } = [];
}

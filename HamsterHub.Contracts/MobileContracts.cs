using System.ComponentModel.DataAnnotations;

namespace HamsterHub.Contracts;

public sealed record LoginRequest(
    [Required, MaxLength(256)] string Email,
    [Required, MaxLength(1024)] string Password,
    [MaxLength(32)] string? TwoFactorCode = null);
public sealed record RefreshRequest([Required] string RefreshToken);
public sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);
public sealed record ApiError(string Code, IReadOnlyList<string>? Details = null);
public sealed record MemberDto(int Id, int HouseholdId, string HouseholdName, string Role, bool PictureMode = true);
public sealed record SessionDto(string DisplayName, IReadOnlyList<MemberDto> Memberships);
public sealed record TaskDto(int Id, string PetName, string Name, string ImagePath,
    int Points, string Frequency, bool CanComplete, string? CurrentStatus = null, string? VisualKey = null, string? Instructions = null, bool HelpRequested = false);
public sealed record CareLogDto(int Id, string PetName, string TaskName, string MemberName,
    string Status, int Points, DateTimeOffset CompletedAt, IReadOnlyList<string> Photos, int TaskId = 0, string? Feedback = null);
public sealed record DashboardDto(int Balance, IReadOnlyList<TaskDto> Tasks,
    IReadOnlyList<CareLogDto> History, IReadOnlyList<CareLogDto> PendingApprovals);
public sealed record ReviewRequest(bool Approve, [StringLength(300)] string? Feedback = null);

using HamsterHub.Contracts;
using HamsterHub.Data;
using HamsterHub.Models;
using HamsterHub.Security;
using HamsterHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Controllers.Api;

[ApiController, Route("api/v1"), Authorize(Policy = MobileAuthentication.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MobileCareController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    CareWorkflowService workflow, CareLogService care, PointBalanceService points,
    IStringLocalizer<SharedResource> localizer, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("memberships/{memberId:int}/media")]
    public async Task<IActionResult> Photo(int memberId, [FromQuery] string path, CancellationToken cancellationToken)
    {
        var member = await GetMemberAsync(memberId, cancellationToken);
        if (member is null) return Forbid();
        var segments = path.Split('/');
        if (segments.Length != 4 || segments[0] != "" || segments[1] != "uploads" ||
            !Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(segments[3]), "N", out _))
            return NotFound();
        var contentType = System.IO.Path.GetExtension(segments[3]).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null
        };
        if (contentType is null) return NotFound();
        var isParent = member.MemberRole == HouseholdMemberRole.Parent;
        var allowed = segments[2] switch
        {
            "care-logs" => await db.CareLogPhotos.AnyAsync(p => p.ImagePath == path &&
                p.CareLog.Pet.HouseholdId == member.HouseholdId &&
                p.CareLog.CareTask.HouseholdId == member.HouseholdId &&
                p.CareLog.CareTask.PetId == p.CareLog.PetId &&
                (isParent || p.CareLog.CompletedByUserId == member.UserId), cancellationToken),
            "tasks" => await db.CareTasks.AnyAsync(t => t.ImagePath == path && t.HouseholdId == member.HouseholdId &&
                t.Pet.HouseholdId == member.HouseholdId && (isParent || t.AssignedMemberId == member.Id), cancellationToken),
            "pets" => await db.Pets.AnyAsync(p => p.PhotoPath == path && p.HouseholdId == member.HouseholdId, cancellationToken),
            _ => false
        };
        if (!allowed) return NotFound();
        var file = System.IO.Path.Combine(environment.WebRootPath, "uploads", segments[2], segments[3]);
        return System.IO.File.Exists(file) ? PhysicalFile(file, contentType) : NotFound();
    }

    [HttpGet("me")]
    public async Task<ActionResult<SessionDto>> Me(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();
        var roles = await users.GetRolesAsync(user);
        var members = await db.HouseholdMembers.AsNoTracking().Include(m => m.Household)
            .Where(m => m.UserId == user.Id && m.IsActive).ToListAsync(cancellationToken);
        return new SessionDto(user.DisplayName, members.Where(m => roles.Contains(m.MemberRole.ToString()))
            .Select(m => new MemberDto(m.Id, m.HouseholdId,
                m.Household.IsNameCustomized ? m.Household.Name : localizer["DefaultHouseholdName"].Value,
                m.MemberRole.ToString())).ToList());
    }

    [HttpGet("memberships/{memberId:int}/dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(int memberId, CancellationToken cancellationToken)
    {
        var member = await GetMemberAsync(memberId, cancellationToken);
        if (member is null) return Forbid();
        var tasks = await db.CareTasks.AsNoTracking().Include(t => t.Pet)
            .Include(t => t.CareCategory).Include(t => t.AssignedMember)
            .Where(t => t.HouseholdId == member.HouseholdId && t.AssignedMemberId == member.Id &&
                t.IsActive && t.Pet.IsActive && t.Pet.HouseholdId == member.HouseholdId)
            .ToListAsync(cancellationToken);
        var ownLogs = await ScopedLogs(member.HouseholdId)
            .Where(l => l.CompletedByUserId == member.UserId).ToListAsync(cancellationToken);
        var pending = member.MemberRole == HouseholdMemberRole.Parent
            ? await ScopedLogs(member.HouseholdId).Where(l => l.Status == CareLogStatus.Pending)
                .ToListAsync(cancellationToken) : [];
        var now = care.GetUtcNow();
        return new DashboardDto(await points.GetBalanceAsync(member.HouseholdId, member.UserId, cancellationToken),
            tasks.OrderBy(t => t.Pet.Name).Select(t =>
            {
                var cutoff = care.GetEarliestAllowed(t.Frequency, now);
                return new TaskDto(t.Id, t.Pet.Name, CategoryName(t.CareCategory), ImagePath(t),
                    t.PointValue, t.Frequency.ToString(), care.CanCompleteTask(t, member) &&
                    (cutoff is null || !ownLogs.Any(l => l.CareTaskId == t.Id &&
                        l.Status != CareLogStatus.Rejected && l.CompletedAt >= cutoff)));
            }).ToList(),
            ownLogs.OrderByDescending(l => l.CompletedAt).Take(50).Select(ToDto).ToList(),
            pending.OrderByDescending(l => l.CompletedAt).Select(ToDto).ToList());
    }

    [HttpPost("memberships/{memberId:int}/tasks/{taskId:int}/complete")]
    [Consumes("multipart/form-data"), RequestSizeLimit(45 * 1024 * 1024)]
    public async Task<IActionResult> Complete(int memberId, int taskId,
        [FromForm] List<IFormFile>? photos, CancellationToken cancellationToken)
    {
        var member = await GetMemberAsync(memberId, cancellationToken);
        if (member is null) return Forbid();
        var result = await workflow.CompleteAsync(member, taskId, photos, cancellationToken);
        return WorkflowResponse(result);
    }

    [HttpPost("memberships/{memberId:int}/care-logs/{logId:int}/review")]
    public async Task<IActionResult> Review(int memberId, int logId, ReviewRequest request,
        CancellationToken cancellationToken)
    {
        var member = await GetMemberAsync(memberId, cancellationToken);
        if (member?.MemberRole != HouseholdMemberRole.Parent) return Forbid();
        return WorkflowResponse(await workflow.ReviewAsync(member, logId,
            request.Approve ? CareLogStatus.Approved : CareLogStatus.Rejected, cancellationToken));
    }

    private IActionResult WorkflowResponse(CareWorkflowResult result) => result.Error switch
    {
        null => Ok(new { id = result.Log!.Id, status = result.Log.Status.ToString() }),
        "Forbidden" => Forbid(),
        "NotFound" => NotFound(new ApiError(result.Error)),
        "TaskAlreadyRecorded" => Conflict(new ApiError(result.Error)),
        _ => BadRequest(new ApiError(result.Error))
    };

    private async Task<HouseholdMember?> GetMemberAsync(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return null;
        var member = await db.HouseholdMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == user.Id && m.IsActive, cancellationToken);
        return member is not null && await users.IsInRoleAsync(user, member.MemberRole.ToString()) ? member : null;
    }

    private IQueryable<CareLog> ScopedLogs(int householdId) => db.CareLogs.AsNoTracking()
        .Include(l => l.Photos).Include(l => l.CompletedByUser)
        .Include(l => l.Pet).Include(l => l.CareTask).ThenInclude(t => t.CareCategory)
        .Where(l => l.Pet.HouseholdId == householdId && l.CareTask.HouseholdId == householdId &&
            l.CareTask.PetId == l.PetId);

    private string CategoryName(CareCategory category) => category.Code is { } code
        ? localizer[$"Category_{code}"].Value : category.CustomName ?? "";
    private static string ImagePath(CareTask task) => task.ImagePath ?? (task.CareCategory.Code is
        "Feeding" or "Water" or "Cleaning" or "Playing" or "Health"
            ? $"/images/tasks/{task.CareCategory.Code.ToLowerInvariant()}.webp"
            : task.Pet.PhotoPath ?? "/images/tasks/playing.webp");
    private CareLogDto ToDto(CareLog log) => new(log.Id, log.Pet.Name, CategoryName(log.CareTask.CareCategory),
        log.CompletedByUser.DisplayName, log.Status.ToString(), log.PointsAwarded, log.CompletedAt,
        log.Photos.OrderBy(p => p.Id).Select(p => p.ImagePath).ToList());
}

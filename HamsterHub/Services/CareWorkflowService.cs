using System.Data;
using HamsterHub.Data;
using HamsterHub.Models;
using Microsoft.EntityFrameworkCore;

namespace HamsterHub.Services;

public sealed record CareWorkflowResult(CareLog? Log = null, string? Error = null)
{
    public bool Success => Error is null;
}

// The MVC website and mobile API use the same transaction and authorization rules.
public sealed class CareWorkflowService(
    ApplicationDbContext db, CareLogService care, PointBalanceService points,
    UploadedImageService images)
{
    public async Task<CareWorkflowResult> CompleteAsync(
        HouseholdMember member, int taskId, IReadOnlyList<IFormFile>? photos = null,
        CancellationToken cancellationToken = default)
    {
        if (!member.IsActive || member.MemberRole is not (HouseholdMemberRole.Child or HouseholdMemberRole.Parent))
            return new(Error: "Forbidden");
        photos ??= [];
        if (photos.Count > 8) return new(Error: "TooManyTaskPhotos");

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var task = await db.CareTasks.Include(t => t.Pet).Include(t => t.AssignedMember)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.HouseholdId == member.HouseholdId,
                cancellationToken);
        if (task is null || !care.CanCompleteTask(task, member)) return new(Error: "Forbidden");

        var now = care.GetUtcNow();
        var cutoff = care.GetEarliestAllowed(task.Frequency, now);
        // Evaluate DateTimeOffset in memory for parity between SQL Server and SQLite tests.
        var previous = await db.CareLogs.Where(l => l.CareTaskId == task.Id &&
                l.PetId == task.PetId && l.CompletedByUserId == member.UserId &&
                l.Status != CareLogStatus.Rejected)
            .Select(l => l.CompletedAt).ToListAsync(cancellationToken);
        if (cutoff is not null && previous.Any(time => time >= cutoff))
            return new(Error: "TaskAlreadyRecorded");

        var saved = new List<string>();
        var committed = false;
        try
        {
            foreach (var photo in photos)
            {
                var result = await images.SaveAsync(photo, "care-logs", "InvalidTaskPhoto", "TaskPhotoTooLarge");
                if (!result.Success) return new(Error: result.ErrorKey);
                saved.Add(result.Path!);
            }
            var log = member.MemberRole == HouseholdMemberRole.Parent
                ? care.CreateParentCompletion(task, member,
                    await points.GetBalanceAsync(member.HouseholdId, member.UserId, cancellationToken), now)
                : care.CreateChildCompletion(task, member, now);
            log.Photos = saved.Select(path => new CareLogPhoto { ImagePath = path }).ToList();
            db.CareLogs.Add(log);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new(log);
        }
        finally
        {
            if (!committed)
                foreach (var path in saved) images.Delete(path, "care-logs");
        }
    }

    public async Task<CareWorkflowResult> ReviewAsync(HouseholdMember parent, int logId,
        CareLogStatus decision, CancellationToken cancellationToken = default)
    {
        if (!parent.IsActive || parent.MemberRole != HouseholdMemberRole.Parent)
            return new(Error: "Forbidden");
        if (decision is not (CareLogStatus.Approved or CareLogStatus.Rejected))
            return new(Error: "InvalidDecision");
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var log = await db.CareLogs.Include(l => l.Pet).Include(l => l.CareTask)
            .FirstOrDefaultAsync(l => l.Id == logId && l.Pet.HouseholdId == parent.HouseholdId &&
                l.CareTask.HouseholdId == parent.HouseholdId && l.CareTask.PetId == l.PetId,
                cancellationToken);
        if (log is null || !care.CanReviewCareLog(log, parent)) return new(Error: "NotFound");
        care.ReviewCareLog(log, parent, decision,
            decision == CareLogStatus.Approved
                ? await points.GetBalanceAsync(parent.HouseholdId, log.CompletedByUserId, cancellationToken) : 0,
            care.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(log);
    }
}

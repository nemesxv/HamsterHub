using HamsterHub.Models;

namespace HamsterHub.Services;

public sealed class CareLogService(TimeProvider timeProvider)
{
    public DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();

    public DateTimeOffset? GetEarliestAllowed(
        CareTaskFrequency frequency,
        DateTimeOffset now) =>
        frequency switch
        {
            CareTaskFrequency.Daily =>
                new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero),
            CareTaskFrequency.Weekly => now.AddDays(-7),
            CareTaskFrequency.AsNeeded => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(frequency), frequency, "Unknown care task frequency.")
        };

    public bool CanCompleteTask(CareTask careTask, HouseholdMember member) =>
        member.IsActive &&
        careTask.IsActive &&
        careTask.HouseholdId == member.HouseholdId &&
        careTask.AssignedMemberId == member.Id &&
        (careTask.PetId == null || (careTask.Pet is { IsActive: true } &&
        careTask.Pet.HouseholdId == member.HouseholdId)) &&
        careTask.AssignedMember is { IsActive: true } &&
        careTask.AssignedMember.Id == member.Id &&
        careTask.AssignedMember.HouseholdId == member.HouseholdId;

    public bool CanReviewCareLog(CareLog careLog, HouseholdMember parent) =>
        parent.IsActive &&
        parent.MemberRole == HouseholdMemberRole.Parent &&
        careLog.Status == CareLogStatus.Pending &&
        (careLog.PetId == null || (careLog.Pet is not null &&
        careLog.Pet.HouseholdId == parent.HouseholdId)) &&
        careLog.CareTask is not null &&
        careLog.CareTask.HouseholdId == parent.HouseholdId;

    public CareLog CreateChildCompletion(
        CareTask careTask,
        HouseholdMember child,
        DateTimeOffset completedAt)
    {
        if (child.MemberRole != HouseholdMemberRole.Child ||
            !CanCompleteTask(careTask, child))
        {
            throw new InvalidOperationException(
                "The child cannot complete this care task.");
        }

        return CreateCompletion(careTask, child.UserId, completedAt);
    }

    public CareLog CreateParentCompletion(
        CareTask careTask,
        HouseholdMember parent,
        int previousBalance,
        DateTimeOffset completedAt)
    {
        if (parent.MemberRole != HouseholdMemberRole.Parent ||
            !CanCompleteTask(careTask, parent))
        {
            throw new InvalidOperationException(
                "The parent cannot complete this care task.");
        }

        var careLog = CreateCompletion(careTask, parent.UserId, completedAt);
        careLog.Status = CareLogStatus.Approved;
        careLog.ApprovedByUserId = parent.UserId;
        careLog.ApprovedAt = completedAt;
        careLog.PointsTotalAfterApproval =
            previousBalance + careLog.PointsAwarded;
        return careLog;
    }

    public void ReviewCareLog(
        CareLog careLog,
        HouseholdMember parent,
        CareLogStatus decision,
        int previousBalance,
        DateTimeOffset reviewedAt)
    {
        if (decision is not (CareLogStatus.Approved or CareLogStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decision), decision, "Unknown care log review decision.");
        }

        if (!CanReviewCareLog(careLog, parent))
        {
            throw new InvalidOperationException(
                "The parent cannot review this care log.");
        }

        careLog.Status = decision;
        careLog.ApprovedByUserId = parent.UserId;
        careLog.ApprovedAt = reviewedAt;
        careLog.PointsTotalAfterApproval = decision == CareLogStatus.Approved
            ? previousBalance + careLog.PointsAwarded
            : null;
    }

    private static CareLog CreateCompletion(
        CareTask careTask,
        string completedByUserId,
        DateTimeOffset completedAt) =>
        new()
        {
            PetId = careTask.PetId,
            CareTaskId = careTask.Id,
            CompletedByUserId = completedByUserId,
            CompletedAt = completedAt,
            PointsAwarded = careTask.PointValue,
            Status = CareLogStatus.Pending
        };
}

using HamsterHub.Models;
using HamsterHub.Services;

namespace HamsterHub.Tests.Services;

public sealed class CareLogServiceTests
{
    private static readonly DateTimeOffset FrozenUtcNow =
        new(2026, 7, 28, 14, 30, 0, TimeSpan.Zero);

    private readonly CareLogService service = new(new FixedTimeProvider(FrozenUtcNow));

    [Fact]
    public void GetUtcNow_ReturnsTimeFromProvider()
    {
        Assert.Equal(FrozenUtcNow, service.GetUtcNow());
    }

    [Fact]
    public void GetEarliestAllowed_Daily_ReturnsStartOfCurrentUtcDay()
    {
        var now = new DateTimeOffset(
            2026, 7, 29, 2, 30, 0, TimeSpan.FromHours(3));

        var cutoff = service.GetEarliestAllowed(CareTaskFrequency.Daily, now);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            cutoff);
    }

    [Fact]
    public void GetEarliestAllowed_Weekly_ReturnsExactRollingSevenDayCutoff()
    {
        var now = new DateTimeOffset(
            2026, 7, 28, 18, 15, 42, TimeSpan.FromHours(3));

        var cutoff = service.GetEarliestAllowed(CareTaskFrequency.Weekly, now);

        Assert.Equal(now.AddDays(-7), cutoff);
    }

    [Fact]
    public void GetEarliestAllowed_AsNeeded_ReturnsNoCutoff()
    {
        var cutoff = service.GetEarliestAllowed(
            CareTaskFrequency.AsNeeded,
            FrozenUtcNow);

        Assert.Null(cutoff);
    }

    [Fact]
    public void GetEarliestAllowed_UnknownFrequency_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.GetEarliestAllowed((CareTaskFrequency)999, FrozenUtcNow));

        Assert.Equal("frequency", exception.ParamName);
    }

    [Fact]
    public void GetCurrentPeriodStatus_DistinguishesPendingApprovedAndRejected()
    {
        var (task, _) = CreateValidAssignment(HouseholdMemberRole.Child);
        var log = new CareLog
        {
            CareTaskId = task.Id,
            CompletedAt = FrozenUtcNow.AddHours(-1),
            Status = CareLogStatus.Pending
        };

        Assert.Equal(CareLogStatus.Pending, service.GetCurrentPeriodStatus(task, [log], FrozenUtcNow));
        log.Status = CareLogStatus.Approved;
        Assert.Equal(CareLogStatus.Approved, service.GetCurrentPeriodStatus(task, [log], FrozenUtcNow));
        log.Status = CareLogStatus.Rejected;
        Assert.Null(service.GetCurrentPeriodStatus(task, [log], FrozenUtcNow));
    }

    [Fact]
    public void ReviewCareLog_AllowsHistoricalPetAfterTaskPetChanges()
    {
        var (careLog, parent) = CreateReviewScenario();
        careLog.CareTask.PetId = careLog.PetId + 1;

        service.ReviewCareLog(careLog, parent, CareLogStatus.Approved, 10, FrozenUtcNow);

        Assert.Equal(CareLogStatus.Approved, careLog.Status);
        Assert.Equal(15, careLog.PointsTotalAfterApproval);
    }

    [Fact]
    public void CanCompleteTask_ReturnsTrue_ForValidActiveAssignment()
    {
        var (careTask, member) = CreateValidAssignment(HouseholdMemberRole.Child);

        Assert.True(service.CanCompleteTask(careTask, member));
    }

    [Fact]
    public void CanCompleteTask_ReturnsFalse_WhenTaskBelongsToAnotherHousehold()
    {
        var (careTask, member) = CreateValidAssignment(HouseholdMemberRole.Child);
        careTask.HouseholdId = member.HouseholdId + 1;

        Assert.False(service.CanCompleteTask(careTask, member));
    }

    [Fact]
    public void CanCompleteTask_ReturnsFalse_WhenTaskIsAssignedToAnotherMember()
    {
        var (careTask, member) = CreateValidAssignment(HouseholdMemberRole.Child);
        careTask.AssignedMemberId = member.Id + 1;

        Assert.False(service.CanCompleteTask(careTask, member));
    }

    [Fact]
    public void CanCompleteTask_ReturnsFalse_WhenAssignedMemberNavigationIsForeign()
    {
        var (careTask, member) = CreateValidAssignment(HouseholdMemberRole.Child);
        careTask.AssignedMember = new HouseholdMember
        {
            Id = member.Id,
            HouseholdId = member.HouseholdId + 1,
            UserId = member.UserId,
            MemberRole = member.MemberRole
        };

        Assert.False(service.CanCompleteTask(careTask, member));
    }

    [Fact]
    public void CanCompleteTask_ReturnsFalse_WhenPetBelongsToAnotherHousehold()
    {
        var (careTask, member) = CreateValidAssignment(HouseholdMemberRole.Child);
        Assert.NotNull(careTask.Pet);
        careTask.Pet.HouseholdId = member.HouseholdId + 1;

        Assert.False(service.CanCompleteTask(careTask, member));
    }

    [Theory]
    [InlineData(InactiveGraphPart.Member)]
    [InlineData(InactiveGraphPart.Task)]
    [InlineData(InactiveGraphPart.Pet)]
    [InlineData(InactiveGraphPart.AssignedMember)]
    public void CanCompleteTask_ReturnsFalse_WhenRequiredGraphPartIsInactive(
        InactiveGraphPart inactivePart)
    {
        var (careTask, member) = CreateValidAssignment(HouseholdMemberRole.Child);

        switch (inactivePart)
        {
            case InactiveGraphPart.Member:
                member.IsActive = false;
                break;
            case InactiveGraphPart.Task:
                careTask.IsActive = false;
                break;
            case InactiveGraphPart.Pet:
                Assert.NotNull(careTask.Pet);
                careTask.Pet.IsActive = false;
                break;
            case InactiveGraphPart.AssignedMember:
                careTask.AssignedMember.IsActive = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(inactivePart), inactivePart, null);
        }

        Assert.False(service.CanCompleteTask(careTask, member));
    }

    [Fact]
    public void CreateChildCompletion_CreatesPendingLogWithPointSnapshot()
    {
        var (careTask, child) = CreateValidAssignment(HouseholdMemberRole.Child);
        careTask.PointValue = 12;

        var careLog = service.CreateChildCompletion(careTask, child, FrozenUtcNow);
        careTask.PointValue = 99;

        Assert.Equal(careTask.PetId, careLog.PetId);
        Assert.Equal(careTask.Id, careLog.CareTaskId);
        Assert.Equal(child.UserId, careLog.CompletedByUserId);
        Assert.Equal(FrozenUtcNow, careLog.CompletedAt);
        Assert.Equal(12, careLog.PointsAwarded);
        Assert.Equal(CareLogStatus.Pending, careLog.Status);
        Assert.Null(careLog.ApprovedByUserId);
        Assert.Null(careLog.ApprovedAt);
        Assert.Null(careLog.PointsTotalAfterApproval);
    }

    [Fact]
    public void CreateChildCompletion_ThrowsWhenAssignedMemberIsNotAChild()
    {
        var (careTask, parent) = CreateValidAssignment(HouseholdMemberRole.Parent);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateChildCompletion(careTask, parent, FrozenUtcNow));
    }

    [Fact]
    public void CreateParentCompletion_AutoApprovesAndCapturesRunningTotal()
    {
        var (careTask, parent) = CreateValidAssignment(HouseholdMemberRole.Parent);
        careTask.PointValue = 8;

        var careLog = service.CreateParentCompletion(
            careTask,
            parent,
            previousBalance: 21,
            FrozenUtcNow);
        careTask.PointValue = 100;

        Assert.Equal(parent.UserId, careLog.CompletedByUserId);
        Assert.Equal(FrozenUtcNow, careLog.CompletedAt);
        Assert.Equal(8, careLog.PointsAwarded);
        Assert.Equal(CareLogStatus.Approved, careLog.Status);
        Assert.Equal(parent.UserId, careLog.ApprovedByUserId);
        Assert.Equal(FrozenUtcNow, careLog.ApprovedAt);
        Assert.Equal(29, careLog.PointsTotalAfterApproval);
    }

    [Fact]
    public void CreateParentCompletion_ThrowsWhenAssignedMemberIsNotAParent()
    {
        var (careTask, child) = CreateValidAssignment(HouseholdMemberRole.Child);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateParentCompletion(
                careTask,
                child,
                previousBalance: 0,
                FrozenUtcNow));
    }

    [Fact]
    public void ReviewCareLog_ApprovalUsesStoredPointsAwarded()
    {
        var (careLog, parent) = CreateReviewScenario();
        careLog.PointsAwarded = 6;
        careLog.CareTask.PointValue = 100;
        var reviewedAt = FrozenUtcNow.AddMinutes(10);

        service.ReviewCareLog(
            careLog,
            parent,
            CareLogStatus.Approved,
            previousBalance: 14,
            reviewedAt);

        Assert.Equal(CareLogStatus.Approved, careLog.Status);
        Assert.Equal(parent.UserId, careLog.ApprovedByUserId);
        Assert.Equal(reviewedAt, careLog.ApprovedAt);
        Assert.Equal(20, careLog.PointsTotalAfterApproval);
    }

    [Fact]
    public void ReviewCareLog_RejectionLeavesRunningTotalNull()
    {
        var (careLog, parent) = CreateReviewScenario();
        careLog.PointsTotalAfterApproval = 500;
        var reviewedAt = FrozenUtcNow.AddMinutes(10);

        service.ReviewCareLog(
            careLog,
            parent,
            CareLogStatus.Rejected,
            previousBalance: 14,
            reviewedAt);

        Assert.Equal(CareLogStatus.Rejected, careLog.Status);
        Assert.Equal(parent.UserId, careLog.ApprovedByUserId);
        Assert.Equal(reviewedAt, careLog.ApprovedAt);
        Assert.Null(careLog.PointsTotalAfterApproval);
    }

    [Fact]
    public void ReviewCareLog_InvalidDecision_ThrowsWithoutMutatingLog()
    {
        var (careLog, parent) = CreateReviewScenario();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ReviewCareLog(
                careLog,
                parent,
                CareLogStatus.Pending,
                previousBalance: 10,
                FrozenUtcNow));

        Assert.Equal("decision", exception.ParamName);
        Assert.Equal(CareLogStatus.Pending, careLog.Status);
        Assert.Null(careLog.ApprovedByUserId);
        Assert.Null(careLog.ApprovedAt);
        Assert.Null(careLog.PointsTotalAfterApproval);
    }

    [Theory]
    [InlineData(UnauthorizedReviewReason.InactiveParent)]
    [InlineData(UnauthorizedReviewReason.ChildRole)]
    [InlineData(UnauthorizedReviewReason.ForeignHousehold)]
    [InlineData(UnauthorizedReviewReason.ForeignCareTask)]
    [InlineData(UnauthorizedReviewReason.ForeignHistoricalPet)]
    [InlineData(UnauthorizedReviewReason.AlreadyReviewed)]
    public void ReviewCareLog_UnauthorizedReviewerOrLog_Throws(
        UnauthorizedReviewReason reason)
    {
        var (careLog, parent) = CreateReviewScenario();

        switch (reason)
        {
            case UnauthorizedReviewReason.InactiveParent:
                parent.IsActive = false;
                break;
            case UnauthorizedReviewReason.ChildRole:
                parent.MemberRole = HouseholdMemberRole.Child;
                break;
            case UnauthorizedReviewReason.ForeignHousehold:
                Assert.NotNull(careLog.Pet);
                parent.HouseholdId = careLog.Pet.HouseholdId + 1;
                break;
            case UnauthorizedReviewReason.ForeignCareTask:
                careLog.CareTask.HouseholdId = parent.HouseholdId + 1;
                break;
            case UnauthorizedReviewReason.ForeignHistoricalPet:
                careLog.Pet!.HouseholdId = parent.HouseholdId + 1;
                break;
            case UnauthorizedReviewReason.AlreadyReviewed:
                careLog.Status = CareLogStatus.Approved;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }

        Assert.Throws<InvalidOperationException>(() =>
            service.ReviewCareLog(
                careLog,
                parent,
                CareLogStatus.Approved,
                previousBalance: 10,
                FrozenUtcNow));
    }

    private static (CareTask CareTask, HouseholdMember Member) CreateValidAssignment(
        HouseholdMemberRole role)
    {
        const int householdId = 10;
        var member = new HouseholdMember
        {
            Id = 20,
            HouseholdId = householdId,
            UserId = role == HouseholdMemberRole.Parent ? "parent-user" : "child-user",
            MemberRole = role,
            IsActive = true
        };
        var pet = new Pet
        {
            Id = 30,
            HouseholdId = householdId,
            Name = "Peanut",
            Species = "Hamster",
            IsActive = true
        };
        var careTask = new CareTask
        {
            Id = 40,
            HouseholdId = householdId,
            PetId = pet.Id,
            AssignedMemberId = member.Id,
            PointValue = 5,
            Frequency = CareTaskFrequency.Daily,
            IsActive = true,
            Pet = pet,
            AssignedMember = member
        };

        return (careTask, member);
    }

    private static (CareLog CareLog, HouseholdMember Parent) CreateReviewScenario()
    {
        var (careTask, parent) = CreateValidAssignment(HouseholdMemberRole.Parent);
        var careLog = new CareLog
        {
            Id = 50,
            PetId = careTask.PetId,
            CareTaskId = careTask.Id,
            CompletedByUserId = "child-user",
            CompletedAt = FrozenUtcNow.AddHours(-1),
            PointsAwarded = 5,
            Status = CareLogStatus.Pending,
            Pet = careTask.Pet,
            CareTask = careTask
        };

        return (careLog, parent);
    }

    public enum InactiveGraphPart
    {
        Member,
        Task,
        Pet,
        AssignedMember
    }

    public enum UnauthorizedReviewReason
    {
        InactiveParent,
        ChildRole,
        ForeignHousehold,
        ForeignCareTask,
        ForeignHistoricalPet,
        AlreadyReviewed
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

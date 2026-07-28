using HamsterHub.Models;
using HamsterHub.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HamsterHub.Tests.Controllers;

public sealed class DashboardControllerCareLogTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 7, 28, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteTask_PersistsPendingPointSnapshot_WhenTaskValueChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string childUserId = "snapshot-child";
        int careTaskId;

        await using (var arrangeContext = database.CreateContext())
        {
            var household = CreateHousehold("Snapshot household");
            var child = CreateUser(childUserId, "Snapshot child");
            var membership = CreateMember(
                household,
                child,
                HouseholdMemberRole.Child);
            var pet = CreatePet(household, "Mochi");
            var careTask = CreateTask(
                household,
                pet,
                membership,
                pointValue: 7);
            arrangeContext.CareTasks.Add(careTask);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            careTaskId = careTask.Id;
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(childUserId, "Child");

            var result = await controller.CompleteTask(careTaskId, photos: null);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.Child), redirect.ActionName);
        }

        await using (var updateContext = database.CreateContext())
        {
            var careLog = await updateContext.CareLogs.SingleAsync(cancellationToken);
            Assert.Equal(CareLogStatus.Pending, careLog.Status);
            Assert.Equal(childUserId, careLog.CompletedByUserId);
            Assert.Equal(TestTime, careLog.CompletedAt);
            Assert.Equal(7, careLog.PointsAwarded);
            Assert.Null(careLog.PointsTotalAfterApproval);

            var careTask = await updateContext.CareTasks.SingleAsync(
                task => task.Id == careTaskId,
                cancellationToken);
            careTask.PointValue = 99;
            await updateContext.SaveChangesAsync(cancellationToken);
        }

        await using var assertContext = database.CreateContext();
        var persistedLog = await assertContext.CareLogs.SingleAsync(cancellationToken);
        Assert.Equal(7, persistedLog.PointsAwarded);
        Assert.Equal(CareLogStatus.Pending, persistedLog.Status);
    }

    [Fact]
    public async Task CompleteTask_ForbidsCurrentAssignment_WhenPetIsForeign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string childUserId = "foreign-pet-child";
        int careTaskId;

        await using (var arrangeContext = database.CreateContext())
        {
            var household = CreateHousehold("Current household");
            var foreignHousehold = CreateHousehold("Foreign household");
            var child = CreateUser(childUserId, "Current child");
            var membership = CreateMember(
                household,
                child,
                HouseholdMemberRole.Child);
            var foreignPet = CreatePet(foreignHousehold, "Foreign pet");
            var careTask = CreateTask(
                household,
                foreignPet,
                membership,
                pointValue: 7);
            arrangeContext.CareTasks.Add(careTask);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            careTaskId = careTask.Id;

            Assert.Equal(household.Id, careTask.HouseholdId);
            Assert.Equal(household.Id, membership.HouseholdId);
            Assert.Equal(foreignHousehold.Id, foreignPet.HouseholdId);
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(childUserId, "Child");

            var result = await controller.CompleteTask(careTaskId, photos: null);

            Assert.IsType<ForbidResult>(result);
        }

        await using var assertContext = database.CreateContext();
        Assert.False(await assertContext.CareLogs.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task CompleteTaskAsParent_RunningTotalExcludesForeignHouseholdLogs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string parentUserId = "completion-parent";
        int careTaskId;
        int existingCurrentLogId;

        await using (var arrangeContext = database.CreateContext())
        {
            var household = CreateHousehold("Parent household");
            var parent = CreateUser(parentUserId, "Completing parent");
            var parentMembership = CreateMember(
                household,
                parent,
                HouseholdMemberRole.Parent);
            var pet = CreatePet(household, "Current pet");
            var careTask = CreateTask(
                household,
                pet,
                parentMembership,
                pointValue: 8);

            var foreignHousehold = CreateHousehold("Foreign household");
            var foreignUser = CreateUser("foreign-task-owner", "Foreign task owner");
            var foreignMember = CreateMember(
                foreignHousehold,
                foreignUser,
                HouseholdMemberRole.Parent);
            var foreignPet = CreatePet(foreignHousehold, "Foreign pet");
            var foreignTask = CreateTask(
                foreignHousehold,
                foreignPet,
                foreignMember,
                pointValue: 100);

            var currentLog = CreateApprovedLog(
                pet,
                careTask,
                parent,
                parent,
                pointsAwarded: 11,
                TestTime.AddDays(-2));
            var foreignLog = CreateApprovedLog(
                foreignPet,
                foreignTask,
                parent,
                parent,
                pointsAwarded: 100,
                TestTime.AddDays(-1));

            arrangeContext.CareLogs.AddRange(currentLog, foreignLog);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            careTaskId = careTask.Id;
            existingCurrentLogId = currentLog.Id;
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(parentUserId, "Parent");

            var result = await controller.CompleteTaskAsParent(careTaskId);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.Parent), redirect.ActionName);
        }

        await using var assertContext = database.CreateContext();
        var taskLogs = await assertContext.CareLogs
            .Where(log => log.CareTaskId == careTaskId)
            .ToListAsync(cancellationToken);
        var persistedLog = Assert.Single(
            taskLogs,
            log => log.Id != existingCurrentLogId);
        Assert.Equal(CareLogStatus.Approved, persistedLog.Status);
        Assert.Equal(parentUserId, persistedLog.CompletedByUserId);
        Assert.Equal(parentUserId, persistedLog.ApprovedByUserId);
        Assert.Equal(TestTime, persistedLog.CompletedAt);
        Assert.Equal(TestTime, persistedLog.ApprovedAt);
        Assert.Equal(8, persistedLog.PointsAwarded);
        Assert.Equal(19, persistedLog.PointsTotalAfterApproval);
    }

    [Fact]
    public async Task ReviewCareLog_UsesStoredPointsAndExcludesForeignHouseholdLogs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string parentUserId = "review-parent";
        const string childUserId = "review-child";
        int pendingLogId;

        await using (var arrangeContext = database.CreateContext())
        {
            var household = CreateHousehold("Review household");
            var parent = CreateUser(parentUserId, "Reviewing parent");
            var parentMembership = CreateMember(
                household,
                parent,
                HouseholdMemberRole.Parent);
            var child = CreateUser(childUserId, "Reviewed child");
            var childMembership = CreateMember(
                household,
                child,
                HouseholdMemberRole.Child);
            var pet = CreatePet(household, "Review pet");
            var careTask = CreateTask(
                household,
                pet,
                childMembership,
                pointValue: 99);

            var currentApprovedLog = CreateApprovedLog(
                pet,
                careTask,
                child,
                parent,
                pointsAwarded: 14,
                TestTime.AddDays(-2));
            var pendingLog = new CareLog
            {
                Pet = pet,
                CareTask = careTask,
                CompletedByUser = child,
                CompletedAt = TestTime.AddHours(-1),
                PointsAwarded = 6,
                Status = CareLogStatus.Pending
            };

            var foreignHousehold = CreateHousehold("Foreign review household");
            var foreignUser = CreateUser("foreign-review-owner", "Foreign owner");
            var foreignMember = CreateMember(
                foreignHousehold,
                foreignUser,
                HouseholdMemberRole.Child);
            var foreignPet = CreatePet(foreignHousehold, "Foreign review pet");
            var foreignTask = CreateTask(
                foreignHousehold,
                foreignPet,
                foreignMember,
                pointValue: 100);
            var foreignApprovedLog = CreateApprovedLog(
                foreignPet,
                foreignTask,
                child,
                parent,
                pointsAwarded: 100,
                TestTime.AddDays(-1));

            arrangeContext.HouseholdMembers.Add(parentMembership);
            arrangeContext.CareLogs.AddRange(
                currentApprovedLog,
                pendingLog,
                foreignApprovedLog);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            pendingLogId = pendingLog.Id;
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(parentUserId, "Parent");

            var result = await controller.ReviewCareLog(
                pendingLogId,
                CareLogStatus.Approved);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.Parent), redirect.ActionName);
        }

        await using var assertContext = database.CreateContext();
        var persistedLog = await assertContext.CareLogs.SingleAsync(
            log => log.Id == pendingLogId,
            cancellationToken);
        Assert.Equal(CareLogStatus.Approved, persistedLog.Status);
        Assert.Equal(6, persistedLog.PointsAwarded);
        Assert.Equal(parentUserId, persistedLog.ApprovedByUserId);
        Assert.Equal(TestTime, persistedLog.ApprovedAt);
        Assert.Equal(20, persistedLog.PointsTotalAfterApproval);
    }

    [Fact]
    public async Task ReviewCareLog_ReturnsNotFound_WhenPetIsLocalButTaskIsForeign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string parentUserId = "boundary-parent";
        int careLogId;

        await using (var arrangeContext = database.CreateContext())
        {
            var household = CreateHousehold("Boundary household");
            var parent = CreateUser(parentUserId, "Boundary parent");
            var parentMembership = CreateMember(
                household,
                parent,
                HouseholdMemberRole.Parent);
            var child = CreateUser("boundary-child", "Boundary child");
            var childMembership = CreateMember(
                household,
                child,
                HouseholdMemberRole.Child);
            var localPet = CreatePet(household, "Local pet");

            var foreignHousehold = CreateHousehold("Foreign task household");
            var foreignUser = CreateUser("foreign-assignee", "Foreign assignee");
            var foreignMember = CreateMember(
                foreignHousehold,
                foreignUser,
                HouseholdMemberRole.Child);
            var foreignPet = CreatePet(foreignHousehold, "Foreign task pet");
            var foreignTask = CreateTask(
                foreignHousehold,
                foreignPet,
                foreignMember,
                pointValue: 25);

            var careLog = new CareLog
            {
                Pet = localPet,
                CareTask = foreignTask,
                CompletedByUser = child,
                CompletedAt = TestTime.AddHours(-1),
                PointsAwarded = 25,
                Status = CareLogStatus.Pending
            };
            arrangeContext.HouseholdMembers.AddRange(
                parentMembership,
                childMembership);
            arrangeContext.CareLogs.Add(careLog);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            careLogId = careLog.Id;
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(parentUserId, "Parent");

            var result = await controller.ReviewCareLog(
                careLogId,
                CareLogStatus.Approved);

            Assert.IsType<NotFoundResult>(result);
        }

        await using var assertContext = database.CreateContext();
        var persistedLog = await assertContext.CareLogs.SingleAsync(
            log => log.Id == careLogId,
            cancellationToken);
        Assert.Equal(CareLogStatus.Pending, persistedLog.Status);
        Assert.Null(persistedLog.ApprovedByUserId);
        Assert.Null(persistedLog.ApprovedAt);
        Assert.Null(persistedLog.PointsTotalAfterApproval);
    }

    private static Household CreateHousehold(string name) =>
        new()
        {
            Name = name,
            CreatedAt = TestTime.AddDays(-10)
        };

    private static ApplicationUser CreateUser(string id, string displayName) =>
        new()
        {
            Id = id,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@example.test".ToUpperInvariant(),
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@example.test".ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = TestTime.AddDays(-10)
        };

    private static HouseholdMember CreateMember(
        Household household,
        ApplicationUser user,
        HouseholdMemberRole role) =>
        new()
        {
            Household = household,
            User = user,
            MemberRole = role,
            JoinedAt = TestTime.AddDays(-9)
        };

    private static Pet CreatePet(Household household, string name) =>
        new()
        {
            Household = household,
            Name = name,
            Species = "Hamster",
            CreatedAt = TestTime.AddDays(-8)
        };

    private static CareTask CreateTask(
        Household household,
        Pet pet,
        HouseholdMember assignedMember,
        int pointValue) =>
        new()
        {
            Household = household,
            Pet = pet,
            CareCategoryId = 1,
            AssignedMember = assignedMember,
            PointValue = pointValue,
            Frequency = CareTaskFrequency.AsNeeded
        };

    private static CareLog CreateApprovedLog(
        Pet pet,
        CareTask careTask,
        ApplicationUser completedBy,
        ApplicationUser approvedBy,
        int pointsAwarded,
        DateTimeOffset completedAt) =>
        new()
        {
            Pet = pet,
            CareTask = careTask,
            CompletedByUser = completedBy,
            CompletedAt = completedAt,
            PointsAwarded = pointsAwarded,
            Status = CareLogStatus.Approved,
            ApprovedByUser = approvedBy,
            ApprovedAt = completedAt.AddMinutes(5),
            PointsTotalAfterApproval = pointsAwarded
        };
}

using HamsterHub.Models;
using HamsterHub.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HamsterHub.Tests.Data;

public sealed class ApplicationDbContextTests
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintCheck = 275;
    private const int SqliteConstraintUnique = 2067;
    private static readonly DateTimeOffset TestTime =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureCreated_seeds_built_in_categories_and_identity_roles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var categories = await context.CareCategories
            .AsNoTracking()
            .Where(category => category.HouseholdId == null)
            .OrderBy(category => category.Id)
            .ToListAsync(cancellationToken);
        var roles = await context.Roles
            .AsNoTracking()
            .ToDictionaryAsync(role => role.Id, cancellationToken);

        Assert.Equal([1, 2, 3, 4, 5], categories.Select(category => category.Id));
        Assert.Equal(
            ["Feeding", "Water", "Cleaning", "Playing", "Health"],
            categories.Select(category => category.Code));
        Assert.All(categories, category => Assert.True(category.IsActive));

        Assert.Equal("Parent", roles["parent"].Name);
        Assert.Equal("PARENT", roles["parent"].NormalizedName);
        Assert.Equal("Child", roles["child"].Name);
        Assert.Equal("CHILD", roles["child"].NormalizedName);
    }

    [Fact]
    public async Task SaveChanges_rejects_duplicate_membership_for_same_household_and_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        int householdId;
        const string userId = "duplicate-member-user";

        await using (var arrangeContext = database.CreateContext())
        {
            var household = new Household { Name = "First household", CreatedAt = TestTime };
            var user = CreateUser(userId);
            arrangeContext.HouseholdMembers.Add(new HouseholdMember
            {
                Household = household,
                User = user,
                MemberRole = HouseholdMemberRole.Child,
                JoinedAt = TestTime
            });
            await arrangeContext.SaveChangesAsync(cancellationToken);
            householdId = household.Id;
        }

        await using var duplicateContext = database.CreateContext();
        duplicateContext.HouseholdMembers.Add(new HouseholdMember
        {
            HouseholdId = householdId,
            UserId = userId,
            MemberRole = HouseholdMemberRole.Parent,
            JoinedAt = TestTime.AddMinutes(1)
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicateContext.SaveChangesAsync(cancellationToken));
        AssertSqliteConstraint(exception, SqliteConstraintUnique);
    }

    [Fact]
    public async Task SaveChanges_rejects_negative_care_task_points()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var graph = CreateCareGraph(pointValue: -1);
        context.CareTasks.Add(graph.Task);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(cancellationToken));
        AssertSqliteConstraint(
            exception,
            SqliteConstraintCheck,
            "CK_CareTasks_PointValue_NonNegative");
    }

    [Fact]
    public async Task SaveChanges_rejects_negative_care_log_points()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var graph = CreateCareGraph();
        context.CareLogs.Add(new CareLog
        {
            Pet = graph.Pet,
            CareTask = graph.Task,
            CompletedByUser = graph.User,
            CompletedAt = TestTime,
            PointsAwarded = -1
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(cancellationToken));
        AssertSqliteConstraint(
            exception,
            SqliteConstraintCheck,
            "CK_CareLogs_PointsAwarded_NonNegative");
    }

    [Fact]
    public async Task Deleting_care_log_cascades_to_its_photos()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        int careLogId;
        int careTaskId;

        await using (var arrangeContext = database.CreateContext())
        {
            var graph = CreateCareGraph();
            var careLog = new CareLog
            {
                Pet = graph.Pet,
                CareTask = graph.Task,
                CompletedByUser = graph.User,
                CompletedAt = TestTime,
                PointsAwarded = graph.Task.PointValue,
                Photos =
                [
                    new CareLogPhoto
                    {
                        ImagePath = "/uploads/care-logs/first.webp",
                        CreatedAt = TestTime
                    },
                    new CareLogPhoto
                    {
                        ImagePath = "/uploads/care-logs/second.webp",
                        CreatedAt = TestTime
                    }
                ]
            };
            arrangeContext.CareLogs.Add(careLog);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            careLogId = careLog.Id;
            careTaskId = graph.Task.Id;
        }

        await using (var verifyContext = database.CreateContext())
        {
            Assert.Equal(
                2,
                await verifyContext.CareLogPhotos.CountAsync(
                    photo => photo.CareLogId == careLogId,
                    cancellationToken));
        }

        await using (var deleteContext = database.CreateContext())
        {
            var careLog = await deleteContext.CareLogs.SingleAsync(
                log => log.Id == careLogId,
                cancellationToken);
            deleteContext.CareLogs.Remove(careLog);
            await deleteContext.SaveChangesAsync(cancellationToken);
        }

        await using var assertContext = database.CreateContext();
        Assert.False(await assertContext.CareLogs.AnyAsync(
            log => log.Id == careLogId,
            cancellationToken));
        Assert.False(await assertContext.CareLogPhotos.AnyAsync(
            photo => photo.CareLogId == careLogId,
            cancellationToken));
        Assert.True(await assertContext.CareTasks.AnyAsync(
            task => task.Id == careTaskId,
            cancellationToken));
    }

    private static void AssertSqliteConstraint(
        DbUpdateException exception,
        int expectedExtendedErrorCode,
        string? expectedConstraintName = null)
    {
        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(SqliteConstraint, sqliteException.SqliteErrorCode);
        Assert.Equal(expectedExtendedErrorCode, sqliteException.SqliteExtendedErrorCode);
        if (expectedConstraintName is not null)
        {
            Assert.Contains(expectedConstraintName, sqliteException.Message);
        }
    }

    private static CareGraph CreateCareGraph(int pointValue = 5)
    {
        var household = new Household
        {
            Name = "Test household",
            CreatedAt = TestTime
        };
        var user = CreateUser($"user-{Guid.NewGuid():N}");
        var member = new HouseholdMember
        {
            Household = household,
            User = user,
            MemberRole = HouseholdMemberRole.Child,
            JoinedAt = TestTime
        };
        var pet = new Pet
        {
            Household = household,
            Name = "Peanut",
            Species = "Hamster",
            CreatedAt = TestTime
        };
        var task = new CareTask
        {
            Household = household,
            Pet = pet,
            CareCategoryId = 1,
            AssignedMember = member,
            PointValue = pointValue,
            Frequency = CareTaskFrequency.AsNeeded
        };

        return new CareGraph(user, pet, task);
    }

    private static ApplicationUser CreateUser(string id) =>
        new()
        {
            Id = id,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@example.test".ToUpperInvariant(),
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@example.test".ToUpperInvariant(),
            DisplayName = "Test user",
            CreatedAt = TestTime
        };

    private sealed record CareGraph(
        ApplicationUser User,
        Pet Pet,
        CareTask Task);
}

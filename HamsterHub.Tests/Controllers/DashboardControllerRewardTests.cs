using HamsterHub.Data;
using HamsterHub.Models;
using HamsterHub.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HamsterHub.Tests.Controllers;

public sealed class DashboardControllerRewardTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 7, 29, 0, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddReward_PersistsRewardWithoutImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string parentUserId = "optional-image-parent";
        int childMemberId;
        await using (var arrangeContext = database.CreateContext())
        {
            var household = new Household { Name = "Optional image household" };
            var parent = new HouseholdMember
            {
                Household = household,
                User = CreateUser(parentUserId, "Parent"),
                MemberRole = HouseholdMemberRole.Parent
            };
            var child = new HouseholdMember
            {
                Household = household,
                User = CreateUser("optional-image-child", "Child"),
                MemberRole = HouseholdMemberRole.Child
            };
            arrangeContext.AddRange(parent, child);
            await arrangeContext.SaveChangesAsync(cancellationToken);
            childMemberId = child.Id;
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(parentUserId, "Parent");

            var result = await controller.AddReward(new AddRewardInput
            {
                Name = "Choose dinner",
                PointCost = 15,
                VisibleToMemberIds = [childMemberId]
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.Parent), redirect.ActionName);
        }

        await using var assertContext = database.CreateContext();
        var reward = await assertContext.Rewards
            .Include(item => item.VisibleToMembers)
            .SingleAsync(cancellationToken);
        Assert.Null(reward.ImagePath);
        Assert.Equal(15, reward.PointCost);
        Assert.Equal(childMemberId, Assert.Single(reward.VisibleToMembers).HouseholdMemberId);
    }

    [Fact]
    public async Task RequestReward_PersistsPendingSnapshot_WhenVisibleAndAffordable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        RewardScenario scenario;
        await using (var arrangeContext = database.CreateContext())
        {
            scenario = await SeedScenarioAsync(
                arrangeContext, rewardVisible: true, cancellationToken);
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(scenario.ChildUserId, "Child");

            var result = await controller.RequestReward(scenario.RewardId);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.Child), redirect.ActionName);
        }

        await using var assertContext = database.CreateContext();
        var request = await assertContext.RewardRedemptions.SingleAsync(
            cancellationToken);
        Assert.Equal(RewardRedemptionStatus.Pending, request.Status);
        Assert.Equal("Extra phone time", request.RewardName);
        Assert.Equal(20, request.PointsCost);
        Assert.Equal(scenario.ChildMemberId, request.HouseholdMemberId);
        Assert.Equal(scenario.ChildUserId, request.RequestedByUserId);
        Assert.Equal(TestTime, request.RequestedAt);
    }

    [Fact]
    public async Task RequestReward_ReturnsNotFound_WhenHiddenFromChild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        RewardScenario scenario;
        await using (var arrangeContext = database.CreateContext())
        {
            scenario = await SeedScenarioAsync(
                arrangeContext, rewardVisible: false, cancellationToken);
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(scenario.ChildUserId, "Child");

            var result = await controller.RequestReward(scenario.RewardId);

            Assert.IsType<NotFoundResult>(result);
        }

        await using var assertContext = database.CreateContext();
        Assert.False(await assertContext.RewardRedemptions.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task PurchaseRewardForChild_AllowsHiddenRewardAndDeductsImmediately()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync();
        RewardScenario scenario;
        await using (var arrangeContext = database.CreateContext())
        {
            scenario = await SeedScenarioAsync(
                arrangeContext, rewardVisible: false, cancellationToken);
        }

        await using (var actionContext = database.CreateContext())
        using (var factory = new DashboardControllerTestFactory(actionContext, TestTime))
        {
            var controller = factory.Create(scenario.ParentUserId, "Parent");

            var result = await controller.PurchaseRewardForChild(
                new DirectRewardPurchaseInput
                {
                    RewardId = scenario.RewardId,
                    HouseholdMemberId = scenario.ChildMemberId
                });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.Parent), redirect.ActionName);
        }

        await using var assertContext = database.CreateContext();
        var purchase = await assertContext.RewardRedemptions.SingleAsync(
            cancellationToken);
        Assert.Equal(RewardRedemptionStatus.Approved, purchase.Status);
        Assert.Equal(scenario.ParentUserId, purchase.RequestedByUserId);
        Assert.Equal(scenario.ParentUserId, purchase.ReviewedByUserId);
        Assert.Equal(10, purchase.PointsBalanceAfterApproval);

        using var historyFactory = new DashboardControllerTestFactory(
            assertContext, TestTime);
        var historyController = historyFactory.Create(scenario.ParentUserId, "Parent");
        var historyResult = await historyController.FamilyMember(
            scenario.ChildMemberId);
        var historyView = Assert.IsType<ViewResult>(historyResult);
        var historyModel = Assert.IsType<FamilyMemberHistoryViewModel>(
            historyView.Model);
        var rewardEntry = Assert.Single(
            historyModel.History,
            item => item.Kind == MemberPointHistoryKind.Reward);
        Assert.Equal(-20, rewardEntry.PointsChange);
        Assert.Equal(10, rewardEntry.PointsTotalAfter);
        Assert.Null(rewardEntry.ImagePath);
    }

    private static async Task<RewardScenario> SeedScenarioAsync(
        ApplicationDbContext context,
        bool rewardVisible,
        CancellationToken cancellationToken)
    {
        const string parentUserId = "reward-parent";
        const string childUserId = "reward-child";
        var household = new Household { Name = "Reward household" };
        var parentUser = CreateUser(parentUserId, "Parent");
        var childUser = CreateUser(childUserId, "Child");
        var parent = new HouseholdMember
        {
            Household = household,
            User = parentUser,
            MemberRole = HouseholdMemberRole.Parent
        };
        var child = new HouseholdMember
        {
            Household = household,
            User = childUser,
            MemberRole = HouseholdMemberRole.Child
        };
        var pet = new Pet
        {
            Household = household,
            Name = "Mochi",
            Species = "Hamster"
        };
        var task = new CareTask
        {
            Household = household,
            Pet = pet,
            AssignedMember = child,
            CareCategoryId = 1,
            Frequency = CareTaskFrequency.AsNeeded,
            PointValue = 30
        };
        var careLog = new CareLog
        {
            Pet = pet,
            CareTask = task,
            CompletedByUser = childUser,
            CompletedAt = TestTime.AddDays(-1),
            PointsAwarded = 30,
            Status = CareLogStatus.Approved,
            ApprovedByUser = parentUser,
            ApprovedAt = TestTime.AddDays(-1),
            PointsTotalAfterApproval = 30
        };
        var reward = new Reward
        {
            Household = household,
            Name = "Extra phone time",
            PointCost = 20
        };
        if (rewardVisible)
        {
            reward.VisibleToMembers.Add(new RewardVisibility
            {
                Reward = reward,
                HouseholdMember = child
            });
        }

        context.AddRange(parent, child, careLog, reward);
        await context.SaveChangesAsync(cancellationToken);
        return new RewardScenario(
            parentUserId,
            childUserId,
            child.Id,
            reward.Id);
    }

    private static ApplicationUser CreateUser(string id, string displayName) =>
        new()
        {
            Id = id,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@example.test".ToUpperInvariant(),
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@example.test".ToUpperInvariant(),
            DisplayName = displayName
        };

    private sealed record RewardScenario(
        string ParentUserId,
        string ChildUserId,
        int ChildMemberId,
        int RewardId);
}

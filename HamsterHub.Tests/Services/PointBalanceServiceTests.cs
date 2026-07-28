using HamsterHub.Models;
using HamsterHub.Services;
using HamsterHub.Tests.Infrastructure;

namespace HamsterHub.Tests.Services;

public sealed class PointBalanceServiceTests
{
    [Fact]
    public async Task GetBalanceAsync_SubtractsOnlyApprovedRewardRedemptions()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var child = await SeedPointHistoryAsync(context);
        var service = new PointBalanceService(context);

        var balance = await service.GetBalanceAsync(
            child.HouseholdId,
            child.UserId,
            TestContext.Current.CancellationToken);

        Assert.Equal(18, balance);
    }

    [Fact]
    public async Task GetBalancesAsync_ReturnsZeroForMemberWithoutHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var child = await SeedPointHistoryAsync(context);
        var parent = context.HouseholdMembers.Single(member =>
            member.MemberRole == HouseholdMemberRole.Parent);
        var service = new PointBalanceService(context);

        var balances = await service.GetBalancesAsync(
            child.HouseholdId,
            [child.UserId, parent.UserId],
            TestContext.Current.CancellationToken);

        Assert.Equal(18, balances[child.UserId]);
        Assert.Equal(0, balances[parent.UserId]);
    }

    private static async Task<HouseholdMember> SeedPointHistoryAsync(
        HamsterHub.Data.ApplicationDbContext context)
    {
        var parentUser = new ApplicationUser
        {
            Id = "parent",
            UserName = "parent@example.com",
            NormalizedUserName = "PARENT@EXAMPLE.COM",
            DisplayName = "Parent"
        };
        var childUser = new ApplicationUser
        {
            Id = "child",
            UserName = "child@example.com",
            NormalizedUserName = "CHILD@EXAMPLE.COM",
            DisplayName = "Child"
        };
        var household = new Household { Name = "Test family" };
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
            Name = "Hammy",
            Species = "Hamster"
        };
        var category = new CareCategory { Code = "Feeding" };
        var task = new CareTask
        {
            Household = household,
            Pet = pet,
            AssignedMember = child,
            CareCategory = category,
            PointValue = 30
        };
        var reward = new Reward
        {
            Household = household,
            Name = "Sweets",
            PointCost = 12,
            ImagePath = "/uploads/rewards/sweets.webp"
        };
        context.AddRange(parent, child, pet, category, task, reward);
        context.CareLogs.AddRange(
            new CareLog
            {
                Pet = pet,
                CareTask = task,
                CompletedByUser = childUser,
                PointsAwarded = 30,
                Status = CareLogStatus.Approved
            },
            new CareLog
            {
                Pet = pet,
                CareTask = task,
                CompletedByUser = childUser,
                PointsAwarded = 9,
                Status = CareLogStatus.Pending
            });
        context.RewardRedemptions.AddRange(
            new RewardRedemption
            {
                Household = household,
                Reward = reward,
                HouseholdMember = child,
                RequestedByUser = childUser,
                RewardName = reward.Name,
                PointsCost = 12,
                Status = RewardRedemptionStatus.Approved
            },
            new RewardRedemption
            {
                Household = household,
                Reward = reward,
                HouseholdMember = child,
                RequestedByUser = childUser,
                RewardName = reward.Name,
                PointsCost = 5,
                Status = RewardRedemptionStatus.Pending
            });
        await context.SaveChangesAsync();
        return child;
    }
}

using HamsterHub.Models;
using HamsterHub.Services;

namespace HamsterHub.Tests.Services;

public sealed class RewardServiceTests
{
    private static readonly DateTimeOffset FrozenUtcNow =
        new(2026, 7, 28, 18, 0, 0, TimeSpan.Zero);
    private readonly RewardService service = new(new RewardTimeProvider(FrozenUtcNow));

    [Fact]
    public void CreateRequest_CreatesPendingImmutableSnapshot()
    {
        var (reward, child, _) = CreateScenario();

        var request = service.CreateRequest(reward, child, currentBalance: 25);
        reward.Name = "Changed later";
        reward.PointCost = 99;

        Assert.Equal(RewardRedemptionStatus.Pending, request.Status);
        Assert.Equal("Extra phone time", request.RewardName);
        Assert.Equal("/uploads/rewards/phone.webp", request.RewardImagePath);
        Assert.Equal(20, request.PointsCost);
        Assert.Equal(child.Id, request.HouseholdMemberId);
        Assert.Equal(child.UserId, request.RequestedByUserId);
        Assert.Equal(FrozenUtcNow, request.RequestedAt);
        Assert.Null(request.ReviewedAt);
        Assert.Null(request.PointsBalanceAfterApproval);
    }

    [Fact]
    public void CreateRequest_ThrowsWhenRewardIsHiddenFromChild()
    {
        var (reward, child, _) = CreateScenario();
        reward.VisibleToMembers.Clear();

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateRequest(reward, child, currentBalance: 25));
    }

    [Fact]
    public void CreateRequest_ThrowsWhenChildCannotAffordReward()
    {
        var (reward, child, _) = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateRequest(reward, child, currentBalance: 19));
    }

    [Fact]
    public void CreateDirectPurchase_ApprovesAndCapturesBalance()
    {
        var (reward, child, parent) = CreateScenario();
        reward.VisibleToMembers.Clear();

        var purchase = service.CreateDirectPurchase(
            reward, child, parent, currentBalance: 32);

        Assert.Equal(RewardRedemptionStatus.Approved, purchase.Status);
        Assert.Equal(parent.UserId, purchase.RequestedByUserId);
        Assert.Equal(parent.UserId, purchase.ReviewedByUserId);
        Assert.Equal(FrozenUtcNow, purchase.ReviewedAt);
        Assert.Equal(12, purchase.PointsBalanceAfterApproval);
    }

    [Fact]
    public void Review_ApprovalUsesSnapshotCost()
    {
        var (reward, child, parent) = CreateScenario();
        var request = service.CreateRequest(reward, child, currentBalance: 30);
        reward.PointCost = 2;

        service.Review(
            request,
            parent,
            RewardRedemptionStatus.Approved,
            currentBalance: 25);

        Assert.Equal(RewardRedemptionStatus.Approved, request.Status);
        Assert.Equal(5, request.PointsBalanceAfterApproval);
        Assert.Equal(parent.UserId, request.ReviewedByUserId);
    }

    [Fact]
    public void Review_RejectionDoesNotDeductPoints()
    {
        var (reward, child, parent) = CreateScenario();
        var request = service.CreateRequest(reward, child, currentBalance: 30);

        service.Review(
            request,
            parent,
            RewardRedemptionStatus.Rejected,
            currentBalance: 30);

        Assert.Equal(RewardRedemptionStatus.Rejected, request.Status);
        Assert.Null(request.PointsBalanceAfterApproval);
    }

    [Fact]
    public void Review_ThrowsForParentFromAnotherHousehold()
    {
        var (reward, child, parent) = CreateScenario();
        var request = service.CreateRequest(reward, child, currentBalance: 30);
        parent.HouseholdId++;

        Assert.Throws<InvalidOperationException>(() =>
            service.Review(
                request,
                parent,
                RewardRedemptionStatus.Approved,
                currentBalance: 30));
    }

    private static (Reward Reward, HouseholdMember Child, HouseholdMember Parent)
        CreateScenario()
    {
        var child = new HouseholdMember
        {
            Id = 2,
            HouseholdId = 7,
            UserId = "child",
            MemberRole = HouseholdMemberRole.Child
        };
        var parent = new HouseholdMember
        {
            Id = 1,
            HouseholdId = 7,
            UserId = "parent",
            MemberRole = HouseholdMemberRole.Parent
        };
        var reward = new Reward
        {
            Id = 3,
            HouseholdId = 7,
            Name = "Extra phone time",
            PointCost = 20,
            ImagePath = "/uploads/rewards/phone.webp",
            VisibleToMembers =
            [
                new RewardVisibility { RewardId = 3, HouseholdMemberId = child.Id }
            ]
        };
        return (reward, child, parent);
    }

    private sealed class RewardTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

using HamsterHub.Models;

namespace HamsterHub.Services;

public sealed class RewardService(TimeProvider timeProvider)
{
    public DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();

    public RewardRedemption CreateRequest(
        Reward reward,
        HouseholdMember child,
        int currentBalance)
    {
        ValidateRewardForChild(reward, child, mustBeVisible: true);
        if (currentBalance < reward.PointCost)
        {
            throw new InvalidOperationException("The child cannot afford this reward.");
        }

        return CreateSnapshot(reward, child, child.UserId, GetUtcNow());
    }

    public RewardRedemption CreateDirectPurchase(
        Reward reward,
        HouseholdMember child,
        HouseholdMember parent,
        int currentBalance)
    {
        ValidateRewardForChild(reward, child, mustBeVisible: false);
        ValidateParent(parent, child.HouseholdId);
        if (currentBalance < reward.PointCost)
        {
            throw new InvalidOperationException("The child cannot afford this reward.");
        }

        var now = GetUtcNow();
        var redemption = CreateSnapshot(reward, child, parent.UserId, now);
        Approve(redemption, parent, currentBalance, now);
        return redemption;
    }

    public void Review(
        RewardRedemption redemption,
        HouseholdMember parent,
        RewardRedemptionStatus decision,
        int currentBalance)
    {
        if (decision is not (RewardRedemptionStatus.Approved or RewardRedemptionStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        ValidateParent(parent, redemption.HouseholdId);
        if (redemption.Status != RewardRedemptionStatus.Pending ||
            redemption.HouseholdMember.HouseholdId != parent.HouseholdId)
        {
            throw new InvalidOperationException("The reward request cannot be reviewed.");
        }

        var now = GetUtcNow();
        if (decision == RewardRedemptionStatus.Approved)
        {
            if (currentBalance < redemption.PointsCost)
            {
                throw new InvalidOperationException("The child cannot afford this reward.");
            }

            Approve(redemption, parent, currentBalance, now);
            return;
        }

        redemption.Status = RewardRedemptionStatus.Rejected;
        redemption.ReviewedByUserId = parent.UserId;
        redemption.ReviewedAt = now;
        redemption.PointsBalanceAfterApproval = null;
    }

    private static RewardRedemption CreateSnapshot(
        Reward reward,
        HouseholdMember child,
        string requestedByUserId,
        DateTimeOffset requestedAt) =>
        new()
        {
            HouseholdId = reward.HouseholdId,
            RewardId = reward.Id,
            HouseholdMemberId = child.Id,
            Reward = reward,
            HouseholdMember = child,
            RequestedByUserId = requestedByUserId,
            RequestedAt = requestedAt,
            RewardName = reward.Name,
            RewardImagePath = reward.ImagePath,
            PointsCost = reward.PointCost
        };

    private static void Approve(
        RewardRedemption redemption,
        HouseholdMember parent,
        int currentBalance,
        DateTimeOffset reviewedAt)
    {
        redemption.Status = RewardRedemptionStatus.Approved;
        redemption.ReviewedByUserId = parent.UserId;
        redemption.ReviewedAt = reviewedAt;
        redemption.PointsBalanceAfterApproval = currentBalance - redemption.PointsCost;
    }

    private static void ValidateRewardForChild(
        Reward reward,
        HouseholdMember child,
        bool mustBeVisible)
    {
        if (!reward.IsActive ||
            !child.IsActive ||
            child.MemberRole != HouseholdMemberRole.Child ||
            reward.HouseholdId != child.HouseholdId ||
            (mustBeVisible &&
             !reward.VisibleToMembers.Any(item => item.HouseholdMemberId == child.Id)))
        {
            throw new InvalidOperationException("The reward is not available to this child.");
        }
    }

    private static void ValidateParent(HouseholdMember parent, int householdId)
    {
        if (!parent.IsActive ||
            parent.MemberRole != HouseholdMemberRole.Parent ||
            parent.HouseholdId != householdId)
        {
            throw new InvalidOperationException("The parent cannot manage this reward.");
        }
    }
}

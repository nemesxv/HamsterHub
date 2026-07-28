using HamsterHub.Data;
using HamsterHub.Models;
using Microsoft.EntityFrameworkCore;

namespace HamsterHub.Services;

public sealed class PointBalanceService(ApplicationDbContext dbContext)
{
    public async Task<int> GetBalanceAsync(
        int householdId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var earned = await dbContext.CareLogs
            .Where(log =>
                log.CompletedByUserId == userId &&
                log.Pet.HouseholdId == householdId &&
                log.CareTask.HouseholdId == householdId &&
                log.CareTask.PetId == log.PetId &&
                log.Status == CareLogStatus.Approved)
            .SumAsync(log => (int?)log.PointsAwarded, cancellationToken) ?? 0;
        var spent = await dbContext.RewardRedemptions
            .Where(redemption =>
                redemption.HouseholdId == householdId &&
                redemption.HouseholdMember.UserId == userId &&
                redemption.HouseholdMember.HouseholdId == householdId &&
                redemption.Status == RewardRedemptionStatus.Approved)
            .SumAsync(redemption => (int?)redemption.PointsCost, cancellationToken) ?? 0;

        return earned - spent;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetBalancesAsync(
        int householdId,
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        var balances = ids.ToDictionary(id => id, _ => 0);
        if (ids.Count == 0)
        {
            return balances;
        }

        var earned = await dbContext.CareLogs
            .Where(log =>
                ids.Contains(log.CompletedByUserId) &&
                log.Pet.HouseholdId == householdId &&
                log.CareTask.HouseholdId == householdId &&
                log.CareTask.PetId == log.PetId &&
                log.Status == CareLogStatus.Approved)
            .GroupBy(log => log.CompletedByUserId)
            .Select(group => new { UserId = group.Key, Points = group.Sum(log => log.PointsAwarded) })
            .ToListAsync(cancellationToken);
        var spent = await dbContext.RewardRedemptions
            .Where(redemption =>
                redemption.HouseholdId == householdId &&
                ids.Contains(redemption.HouseholdMember.UserId) &&
                redemption.HouseholdMember.HouseholdId == householdId &&
                redemption.Status == RewardRedemptionStatus.Approved)
            .GroupBy(redemption => redemption.HouseholdMember.UserId)
            .Select(group => new { UserId = group.Key, Points = group.Sum(item => item.PointsCost) })
            .ToListAsync(cancellationToken);

        foreach (var item in earned)
        {
            balances[item.UserId] += item.Points;
        }

        foreach (var item in spent)
        {
            balances[item.UserId] -= item.Points;
        }

        return balances;
    }
}

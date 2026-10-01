using HamsterHub.Contracts;
using HamsterHub.Models;
using HamsterHub.Services;

namespace HamsterHub.Tests.Services;

public sealed class ReminderScheduleTests
{
    [Theory]
    [InlineData("Daily", 2)]
    [InlineData("Weekly", 8)]
    public void RepeatsAtConfiguredLocalTime(string frequency, int nextDay)
    {
        var settings = new TaskReminderDto(new(18, 0), new(2026, 10, 1), "Europe/Moscow");
        var after = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, nextDay, 15, 0, 0, TimeSpan.Zero),
            ReminderSchedule.Next(frequency, settings, after));
    }

    [Fact]
    public void OnceDoesNotRepeatAndUnlimitedCannotHaveReminder()
    {
        var settings = new TaskReminderDto(new(18, 0), new(2026, 10, 1), "UTC");
        var before = new DateTimeOffset(2026, 10, 1, 17, 0, 0, TimeSpan.Zero);
        Assert.Equal(before.AddHours(1), ReminderSchedule.Next("Once", settings, before));
        Assert.Null(ReminderSchedule.Next("Once", settings, before.AddHours(1)));
        Assert.False(ReminderSchedule.IsValid("AsNeeded", settings));
        Assert.True(ReminderSchedule.IsValid("AsNeeded", null));
        Assert.False(ReminderSchedule.IsValid("Daily", settings with { TimeZoneId = "invalid/timezone" }));
    }

    [Fact]
    public void SuppressionSkipsCompletedPeriodAndPermanentlyCompletedOnce()
    {
        var settings = new TaskReminderDto(new(18, 0), new(2026, 10, 1), "UTC");
        var after = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var blocked = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(blocked.AddHours(18), ReminderSchedule.Next("Daily", settings, after, blocked));
        Assert.Null(ReminderSchedule.Next("Once", settings, after, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void LocalTimeSurvivesDaylightSavingChange()
    {
        var settings = new TaskReminderDto(new(18, 0), new(2026, 10, 24), "Europe/Berlin");
        var first = new DateTimeOffset(2026, 10, 24, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero),
            ReminderSchedule.Next("Daily", settings, first));
    }

    [Theory]
    [InlineData(3, 29, 1, 0)]
    [InlineData(10, 25, 1, 30)]
    public void MissingOrRepeatedClockTimeIsResolvedOnce(int month, int day, int hour, int minute)
    {
        var settings = new TaskReminderDto(new(2, 30), new(2026, month, day), "Europe/Berlin");
        var after = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, month, day, hour, minute, 0, TimeSpan.Zero),
            ReminderSchedule.Next("Once", settings, after));
    }

    [Fact]
    public void OnceTaskCannotBeSubmittedAgainEvenWhenHistoryIsOld()
    {
        var service = new CareLogService(TimeProvider.System);
        var task = new CareTask { Id = 1, Frequency = CareTaskFrequency.Once };
        var log = new CareLog { CareTaskId = 1, Status = CareLogStatus.Approved,
            CompletedAt = DateTimeOffset.UtcNow.AddMonths(-2) };
        Assert.Equal(CareLogStatus.Approved, service.GetCurrentPeriodStatus(task, [log], DateTimeOffset.UtcNow));
        log.Status = CareLogStatus.Rejected;
        Assert.Null(service.GetCurrentPeriodStatus(task, [log], DateTimeOffset.UtcNow));
    }
}

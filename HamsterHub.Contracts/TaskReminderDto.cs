namespace HamsterHub.Contracts;

public sealed record TaskReminderDto(TimeOnly Time, DateOnly StartDate, string TimeZoneId);
public sealed record ScheduledTaskReminderDto(int TaskId, string TaskName, string Frequency,
    TaskReminderDto Schedule, DateTimeOffset? SuppressUntil);

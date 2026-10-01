using HamsterHub.Contracts;
using HamsterHub.Models;

namespace HamsterHub.Services;

public static class TaskReminderSettings
{
    public static TaskReminderDto? Read(CareTask task) =>
        task.ReminderTime is { } time && task.ReminderStartDate is { } date &&
        task.ReminderTimeZoneId is { } zone ? new(time, date, zone) : null;

    public static bool IsValid(CareTaskFrequency frequency, TaskReminderDto? reminder) =>
        ReminderSchedule.IsValid(frequency.ToString(), reminder);

    public static void Apply(CareTask task, TaskReminderDto? reminder)
    {
        task.ReminderTime = reminder?.Time;
        task.ReminderStartDate = reminder?.StartDate;
        task.ReminderTimeZoneId = reminder?.TimeZoneId;
    }
}

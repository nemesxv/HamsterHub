namespace HamsterHub.Contracts;

public static class ReminderSchedule
{
    public static bool IsValid(string frequency, TaskReminderDto? schedule)
    {
        if (schedule is null) return true;
        if (frequency is not ("Daily" or "Weekly" or "Once") ||
            string.IsNullOrWhiteSpace(schedule.TimeZoneId) || schedule.TimeZoneId.Length > 100)
            return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    // Calendar arithmetic keeps a parent's wall-clock time stable across DST.
    public static DateTimeOffset? Next(string frequency, TaskReminderDto schedule,
        DateTimeOffset after, DateTimeOffset? suppressUntil = null)
    {
        if (!IsValid(frequency, schedule)) return null;
        if (suppressUntil == DateTimeOffset.MaxValue) return null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var threshold = suppressUntil is { } blocked && blocked > after ? blocked : after;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(threshold, zone).DateTime);
        var date = schedule.StartDate;
        if (frequency == "Daily" && date < today) date = today;
        if (frequency == "Weekly" && date < today)
            date = date.AddDays(((today.DayNumber - date.DayNumber) / 7) * 7);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var local = DateTime.SpecifyKind(date.ToDateTime(schedule.Time), DateTimeKind.Unspecified);
            // A missing spring-forward time is moved to the first valid minute.
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var offset = zone.IsAmbiguousTime(local)
                ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetUtcOffset(local);
            var occurrence = new DateTimeOffset(local, offset).ToUniversalTime();
            if (occurrence > after && occurrence >= threshold) return occurrence;
            if (frequency == "Once") return null;
            date = date.AddDays(frequency == "Weekly" ? 7 : 1);
        }
        return null;
    }
}

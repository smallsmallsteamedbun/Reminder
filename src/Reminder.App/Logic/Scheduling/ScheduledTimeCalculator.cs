using Reminder.App.Logic.Models;

namespace Reminder.App.Logic.Scheduling;

public static class ScheduledTimeCalculator
{
    public static DateTimeOffset? GetNextOccurrence(
        ScheduledTimeSettings settings,
        DateTimeOffset reference,
        TimeZoneInfo? timeZone = null,
        bool inclusive = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        timeZone ??= TimeZoneInfo.Local;

        return settings.Recurrence switch
        {
            ReminderRecurrence.Once =>
                IsEligible(settings.OneTimeAt, reference, inclusive)
                    ? settings.OneTimeAt
                    : null,
            ReminderRecurrence.Daily =>
                GetNextDaily(settings, reference, timeZone, inclusive),
            ReminderRecurrence.Weekly =>
                GetNextWeekly(settings, reference, timeZone, inclusive),
            ReminderRecurrence.Monthly =>
                GetNextMonthly(settings, reference, timeZone, inclusive),
            ReminderRecurrence.Yearly =>
                GetNextYearly(settings, reference, timeZone, inclusive),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.Recurrence,
                "不支持的指定时间频率")
        };
    }

    private static DateTimeOffset? GetNextDaily(
        ScheduledTimeSettings settings,
        DateTimeOffset reference,
        TimeZoneInfo timeZone,
        bool inclusive)
    {
        var localReference = TimeZoneInfo.ConvertTime(reference, timeZone);
        for (var dayOffset = 0; dayOffset <= 2; dayOffset++)
        {
            if (!TryAddDays(localReference.Date, dayOffset, out var date))
            {
                return null;
            }

            var candidate = CreateCandidate(date, settings.TimeOfDay, timeZone);
            if (candidate is not null &&
                IsEligible(candidate.Value, reference, inclusive))
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetNextWeekly(
        ScheduledTimeSettings settings,
        DateTimeOffset reference,
        TimeZoneInfo timeZone,
        bool inclusive)
    {
        var localReference = TimeZoneInfo.ConvertTime(reference, timeZone);
        var initialOffset =
            ((int)settings.DayOfWeek - (int)localReference.DayOfWeek + 7) % 7;

        for (var weekOffset = 0; weekOffset <= 1; weekOffset++)
        {
            var dayOffset = initialOffset + weekOffset * 7;
            if (!TryAddDays(localReference.Date, dayOffset, out var date))
            {
                return null;
            }

            var candidate = CreateCandidate(date, settings.TimeOfDay, timeZone);
            if (candidate is not null &&
                IsEligible(candidate.Value, reference, inclusive))
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetNextMonthly(
        ScheduledTimeSettings settings,
        DateTimeOffset reference,
        TimeZoneInfo timeZone,
        bool inclusive)
    {
        if (settings.DayOfMonth is < 1 or > 31)
        {
            return null;
        }

        var localReference = TimeZoneInfo.ConvertTime(reference, timeZone);
        var year = localReference.Year;
        var month = localReference.Month;

        for (var monthOffset = 0; monthOffset < 2_400; monthOffset++)
        {
            if (settings.DayOfMonth <= DateTime.DaysInMonth(year, month))
            {
                var date = new DateTime(
                    year,
                    month,
                    settings.DayOfMonth,
                    0,
                    0,
                    0,
                    DateTimeKind.Unspecified);
                var candidate = CreateCandidate(
                    date,
                    settings.TimeOfDay,
                    timeZone);
                if (candidate is not null &&
                    IsEligible(candidate.Value, reference, inclusive))
                {
                    return candidate;
                }
            }

            if (!TryMoveToNextMonth(ref year, ref month))
            {
                return null;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetNextYearly(
        ScheduledTimeSettings settings,
        DateTimeOffset reference,
        TimeZoneInfo timeZone,
        bool inclusive)
    {
        if (settings.MonthOfYear is < 1 or > 12 ||
            settings.DayOfMonth is < 1 or > 31)
        {
            return null;
        }

        var localReference = TimeZoneInfo.ConvertTime(reference, timeZone);
        for (var year = localReference.Year; year <= 9_999; year++)
        {
            if (settings.DayOfMonth >
                DateTime.DaysInMonth(year, settings.MonthOfYear))
            {
                continue;
            }

            var date = new DateTime(
                year,
                settings.MonthOfYear,
                settings.DayOfMonth,
                0,
                0,
                0,
                DateTimeKind.Unspecified);
            var candidate = CreateCandidate(
                date,
                settings.TimeOfDay,
                timeZone);
            if (candidate is not null &&
                IsEligible(candidate.Value, reference, inclusive))
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? CreateCandidate(
        DateTime date,
        TimeOnly time,
        TimeZoneInfo timeZone)
    {
        var local = new DateTime(
            date.Year,
            date.Month,
            date.Day,
            time.Hour,
            time.Minute,
            0,
            DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }

        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static bool IsEligible(
        DateTimeOffset candidate,
        DateTimeOffset reference,
        bool inclusive)
    {
        return inclusive
            ? candidate >= reference
            : candidate > reference;
    }

    private static bool TryAddDays(
        DateTime date,
        int days,
        out DateTime result)
    {
        if (days > (DateTime.MaxValue.Date - date).Days)
        {
            result = default;
            return false;
        }

        result = date.AddDays(days);
        return true;
    }

    private static bool TryMoveToNextMonth(ref int year, ref int month)
    {
        if (month < 12)
        {
            month++;
            return true;
        }

        if (year >= 9_999)
        {
            return false;
        }

        year++;
        month = 1;
        return true;
    }
}

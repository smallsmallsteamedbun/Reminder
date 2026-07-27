using Reminder.App.Logic.Models;
using Reminder.App.Logic.State;

namespace Reminder.App.SystemModule.Persistence;

internal static class ReminderStateMapper
{
    public static ReminderStateDocument ToDocument(
        ReminderEngineState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new ReminderStateDocument
        {
            Version = ReminderStateDocument.CurrentVersion,
            SavedAt = state.SavedAt,
            Events = state.Events.Select(ToDocument).ToList(),
            GlobalPause = new ReminderGlobalPauseDocument
            {
                IsPaused = state.GlobalPause.IsPaused,
                Duration = state.GlobalPause.Duration,
                RemainingTicks =
                    state.GlobalPause.Remaining?.Ticks
            },
            PendingMissedEventIds =
                state.PendingMissedEventIds.ToList()
        };
    }

    public static bool TryToEngineState(
        ReminderStateDocument? document,
        out ReminderEngineState state,
        out string errorMessage)
    {
        state = null!;
        errorMessage = string.Empty;
        if (document is null)
        {
            errorMessage = "状态文档为空。";
            return false;
        }

        if (document.Version != ReminderStateDocument.CurrentVersion)
        {
            errorMessage =
                $"不支持的数据结构版本：{document.Version}。";
            return false;
        }

        if (document.Events is null ||
            document.GlobalPause is null ||
            document.PendingMissedEventIds is null)
        {
            errorMessage = "状态文档缺少必要字段。";
            return false;
        }

        try
        {
            state = new ReminderEngineState
            {
                SavedAt = document.SavedAt,
                Events = document.Events.Select(ToEngineState).ToArray(),
                GlobalPause = new ReminderEngineGlobalPauseState
                {
                    IsPaused = document.GlobalPause.IsPaused,
                    Duration = document.GlobalPause.Duration,
                    Remaining =
                        document.GlobalPause.RemainingTicks is null
                            ? null
                            : TimeSpan.FromTicks(
                                document.GlobalPause.RemainingTicks.Value)
                },
                PendingMissedEventIds =
                    document.PendingMissedEventIds.ToArray()
            };
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            OverflowException)
        {
            state = null!;
            errorMessage = $"状态字段无法转换：{exception.Message}";
            return false;
        }
    }

    private static ReminderEventDocument ToDocument(
        ReminderEngineEventState state)
    {
        return new ReminderEventDocument
        {
            Id = state.Id,
            Name = state.Name,
            EventType = state.EventType,
            FixedIntervalTicks = state.FixedInterval.Ticks,
            ScheduledTime = new ReminderScheduledTimeDocument
            {
                Recurrence = state.ScheduledTime.Recurrence,
                OneTimeAt = state.ScheduledTime.OneTimeAt,
                TimeOfDayMinutes =
                    state.ScheduledTime.TimeOfDay.Hour * 60 +
                    state.ScheduledTime.TimeOfDay.Minute,
                DayOfWeek = state.ScheduledTime.DayOfWeek,
                DayOfMonth = state.ScheduledTime.DayOfMonth,
                MonthOfYear = state.ScheduledTime.MonthOfYear
            },
            RemainingOccurrences = state.RemainingOccurrences,
            IsEnabled = state.IsEnabled,
            IsPaused = state.IsPaused,
            FixedUnavailablePolicy = state.FixedUnavailablePolicy,
            FixedUnavailableNotificationPolicy =
                state.FixedUnavailableNotificationPolicy,
            ScheduledUnavailableNotificationPolicy =
                state.ScheduledUnavailableNotificationPolicy,
            FrozenRemainingTicks = state.FrozenRemaining?.Ticks,
            NextScheduledOccurrenceAt =
                state.NextScheduledOccurrenceAt,
            DeferredOccurrenceAt = state.DeferredOccurrenceAt,
            IsExpired = state.IsExpired
        };
    }

    private static ReminderEngineEventState ToEngineState(
        ReminderEventDocument document)
    {
        if (document.ScheduledTime is null)
        {
            throw new ArgumentException("事件缺少指定时间设置。");
        }

        if (document.TimeOfDayMinutesIsInvalid())
        {
            throw new ArgumentException("指定时间中的时分无效。");
        }

        return new ReminderEngineEventState
        {
            Id = document.Id,
            Name = document.Name,
            EventType = document.EventType,
            FixedInterval =
                TimeSpan.FromTicks(document.FixedIntervalTicks),
            ScheduledTime = new ScheduledTimeSettings(
                document.ScheduledTime.Recurrence,
                document.ScheduledTime.OneTimeAt,
                new TimeOnly(
                    document.ScheduledTime.TimeOfDayMinutes / 60,
                    document.ScheduledTime.TimeOfDayMinutes % 60),
                document.ScheduledTime.DayOfWeek,
                document.ScheduledTime.DayOfMonth,
                document.ScheduledTime.MonthOfYear),
            RemainingOccurrences = document.RemainingOccurrences,
            IsEnabled = document.IsEnabled,
            IsPaused = document.IsPaused,
            FixedUnavailablePolicy =
                document.FixedUnavailablePolicy,
            FixedUnavailableNotificationPolicy =
                document.FixedUnavailableNotificationPolicy,
            ScheduledUnavailableNotificationPolicy =
                document.ScheduledUnavailableNotificationPolicy,
            FrozenRemaining =
                document.FrozenRemainingTicks is null
                    ? null
                    : TimeSpan.FromTicks(
                        document.FrozenRemainingTicks.Value),
            NextScheduledOccurrenceAt =
                document.NextScheduledOccurrenceAt,
            DeferredOccurrenceAt = document.DeferredOccurrenceAt,
            IsExpired = document.IsExpired
        };
    }

    private static bool TimeOfDayMinutesIsInvalid(
        this ReminderEventDocument document)
    {
        return document.ScheduledTime.TimeOfDayMinutes is < 0 or >= 1_440;
    }
}

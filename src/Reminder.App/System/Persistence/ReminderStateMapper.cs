using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;
using Reminder.App.Logic.State;
using Reminder.App.SystemModule.AppInfo;
using Reminder.App.SystemModule.Settings;

namespace Reminder.App.SystemModule.Persistence;

internal static class ReminderStateMapper
{
    public static ReminderStateDocument ToDocument(
        ReminderPersistedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.EngineState);
        ArgumentNullException.ThrowIfNull(state.Settings);

        return new ReminderStateDocument
        {
            Version = ReminderStateDocument.CurrentVersion,
            SavedAt = state.EngineState.SavedAt,
            Events = state.EngineState.Events.Select(ToDocument).ToList(),
            GlobalPause = new ReminderGlobalPauseDocument
            {
                IsPaused = state.EngineState.GlobalPause.IsPaused,
                Duration = state.EngineState.GlobalPause.Duration,
                RemainingTicks =
                    state.EngineState.GlobalPause.Remaining?.Ticks
            },
            PendingMissedEventIds =
                state.EngineState.PendingMissedEventIds.ToList(),
            ThemeMode = state.Settings.ThemeMode,
            RenderingMode = state.Settings.RenderingMode,
            StartWithWindows = state.Settings.StartWithWindows,
            SilentStart = state.Settings.SilentStart,
            SnoozeDurationMinutes =
                state.Settings.SnoozeDurationMinutes,
            SnoozeOverflowPolicy =
                state.Settings.SnoozeOverflowPolicy,
            NotificationDisplayDuration =
                state.Settings.NotificationDisplayDuration,
            SearchHistory = state.Settings.SearchHistory.ToList()
        };
    }

    public static bool TryToPersistedState(
        ReminderStateDocument? document,
        out ReminderPersistedState state,
        out string errorMessage)
    {
        state = null!;
        errorMessage = string.Empty;
        if (document is null)
        {
            errorMessage = "状态文档为空。";
            return false;
        }

        if (document.Version is < 1 or > ReminderStateDocument.CurrentVersion)
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
            if (!Enum.IsDefined(document.RenderingMode))
            {
                throw new ArgumentException("渲染模式无效。");
            }

            if (document.Version >= 5 &&
                !Enum.IsDefined(document.ThemeMode))
            {
                throw new ArgumentException("主题模式无效。");
            }

            if (document.Version >= 4)
            {
                if (document.SnoozeDurationMinutes is
                    < ReminderDefaults.MinimumIntervalMinutes or
                    > ReminderDefaults.MaximumIntervalMinutes)
                {
                    throw new ArgumentException(
                        "统一通知延迟时间无效。");
                }

                if (!Enum.IsDefined(document.SnoozeOverflowPolicy))
                {
                    throw new ArgumentException(
                        "通知延迟超出事件间隔时的策略无效。");
                }

                if (!Enum.IsDefined(document.NotificationDisplayDuration))
                {
                    throw new ArgumentException(
                        "Windows 通知显示时长无效。");
                }

                if (document.SearchHistory is null)
                {
                    throw new ArgumentException(
                        "搜索历史记录无效。");
                }
            }

            state = new ReminderPersistedState
            {
                EngineState = new ReminderEngineState
                {
                    SavedAt = document.SavedAt,
                    Events = document.Events
                        .Select(item => ToEngineState(
                            item,
                            document.Version,
                            document.GlobalPause.IsPaused))
                        .ToArray(),
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
                },
                Settings = new ReminderApplicationSettings
                {
                    ThemeMode =
                        document.Version < 5
                            ? ReminderThemeMode.FollowSystem
                            : document.ThemeMode,
                    RenderingMode =
                        document.Version == 1
                            ? ReminderRenderingMode.HardwarePreferred
                            : document.RenderingMode,
                    StartWithWindows =
                        document.Version >= 5 &&
                        document.StartWithWindows,
                    SilentStart =
                        document.Version >= 5 &&
                        document.SilentStart,
                    SnoozeDurationMinutes =
                        document.Version < 4
                            ? (int)ReminderDefaults.SnoozeDuration.TotalMinutes
                            : document.SnoozeDurationMinutes,
                    SnoozeOverflowPolicy =
                        document.Version < 4
                            ? ReminderSnoozeOverflowPolicy
                                .ShortenToFixedInterval
                            : document.SnoozeOverflowPolicy,
                    NotificationDisplayDuration =
                        document.Version < 4
                            ? ReminderNotificationDisplayDuration.Short
                            : document.NotificationDisplayDuration,
                    SearchHistory =
                        document.Version < 4
                            ? []
                            : document.SearchHistory
                }
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
            IsBlockedByGlobalPause = state.IsBlockedByGlobalPause,
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
        ReminderEventDocument document,
        int documentVersion,
        bool isGlobalPaused)
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
            IsBlockedByGlobalPause =
                documentVersion < 3
                    ? isGlobalPaused && document.IsEnabled
                    : document.IsBlockedByGlobalPause,
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

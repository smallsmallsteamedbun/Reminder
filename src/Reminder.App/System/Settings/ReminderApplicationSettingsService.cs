using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.SystemModule.Settings;

public sealed class ReminderApplicationSettingsService :
    IReminderRuntimeSettings
{
    public const int MaximumSearchHistoryCount = 5;

    private readonly object _gate = new();
    private ReminderThemeMode _themeMode;
    private ReminderRenderingMode _renderingMode;
    private bool _startWithWindows;
    private bool _silentStart;
    private int _snoozeDurationMinutes;
    private ReminderSnoozeOverflowPolicy _snoozeOverflowPolicy;
    private ReminderNotificationDisplayDuration _notificationDisplayDuration;
    private readonly List<string> _searchHistory;

    public ReminderApplicationSettingsService(
        ReminderApplicationSettings? initialSettings = null)
    {
        _themeMode =
            initialSettings is not null &&
            Enum.IsDefined(initialSettings.ThemeMode)
                ? initialSettings.ThemeMode
                : ReminderThemeMode.FollowSystem;
        _renderingMode =
            initialSettings?.RenderingMode ??
            ReminderRenderingMode.HardwarePreferred;
        _startWithWindows =
            initialSettings?.StartWithWindows == true;
        _silentStart =
            initialSettings?.SilentStart == true;
        _snoozeDurationMinutes = IsValidSnoozeDuration(
            initialSettings?.SnoozeDurationMinutes)
            ? initialSettings!.SnoozeDurationMinutes
            : (int)ReminderDefaults.SnoozeDuration.TotalMinutes;
        _snoozeOverflowPolicy =
            initialSettings is not null &&
            Enum.IsDefined(initialSettings.SnoozeOverflowPolicy)
                ? initialSettings.SnoozeOverflowPolicy
                : ReminderSnoozeOverflowPolicy.ShortenToFixedInterval;
        _notificationDisplayDuration =
            initialSettings is not null &&
            Enum.IsDefined(initialSettings.NotificationDisplayDuration)
                ? initialSettings.NotificationDisplayDuration
                : ReminderNotificationDisplayDuration.Short;
        _searchHistory = NormalizeSearchHistory(
            initialSettings?.SearchHistory);
    }

    public event EventHandler? SettingsChanged;

    public ReminderThemeMode ThemeMode
    {
        get
        {
            lock (_gate)
            {
                return _themeMode;
            }
        }
    }

    public ReminderRenderingMode RenderingMode
    {
        get
        {
            lock (_gate)
            {
                return _renderingMode;
            }
        }
    }

    public bool StartWithWindows
    {
        get
        {
            lock (_gate)
            {
                return _startWithWindows;
            }
        }
    }

    public bool SilentStart
    {
        get
        {
            lock (_gate)
            {
                return _silentStart;
            }
        }
    }

    public TimeSpan SnoozeDuration
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromMinutes(_snoozeDurationMinutes);
            }
        }
    }

    public int SnoozeDurationMinutes
    {
        get
        {
            lock (_gate)
            {
                return _snoozeDurationMinutes;
            }
        }
    }

    public ReminderSnoozeOverflowPolicy SnoozeOverflowPolicy
    {
        get
        {
            lock (_gate)
            {
                return _snoozeOverflowPolicy;
            }
        }
    }

    public ReminderNotificationDisplayDuration NotificationDisplayDuration
    {
        get
        {
            lock (_gate)
            {
                return _notificationDisplayDuration;
            }
        }
    }

    public IReadOnlyList<string> SearchHistory
    {
        get
        {
            lock (_gate)
            {
                return _searchHistory.ToArray();
            }
        }
    }

    public bool SetRenderingMode(ReminderRenderingMode renderingMode)
    {
        if (!Enum.IsDefined(renderingMode))
        {
            return false;
        }

        lock (_gate)
        {
            if (_renderingMode == renderingMode)
            {
                return false;
            }

            _renderingMode = renderingMode;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SetThemeMode(ReminderThemeMode themeMode)
    {
        if (!Enum.IsDefined(themeMode))
        {
            return false;
        }

        lock (_gate)
        {
            if (_themeMode == themeMode)
            {
                return false;
            }

            _themeMode = themeMode;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SetStartWithWindows(bool enabled)
    {
        lock (_gate)
        {
            if (_startWithWindows == enabled)
            {
                return false;
            }

            _startWithWindows = enabled;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SetSilentStart(bool enabled)
    {
        lock (_gate)
        {
            if (_silentStart == enabled)
            {
                return false;
            }

            _silentStart = enabled;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Apply(ReminderApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var themeMode = Enum.IsDefined(settings.ThemeMode)
            ? settings.ThemeMode
            : ReminderThemeMode.FollowSystem;
        var renderingMode = Enum.IsDefined(settings.RenderingMode)
            ? settings.RenderingMode
            : ReminderRenderingMode.HardwarePreferred;
        var snoozeDurationMinutes = IsValidSnoozeDuration(
            settings.SnoozeDurationMinutes)
            ? settings.SnoozeDurationMinutes
            : (int)ReminderDefaults.SnoozeDuration.TotalMinutes;
        var snoozeOverflowPolicy =
            Enum.IsDefined(settings.SnoozeOverflowPolicy)
                ? settings.SnoozeOverflowPolicy
                : ReminderSnoozeOverflowPolicy.ShortenToFixedInterval;
        var notificationDisplayDuration =
            Enum.IsDefined(settings.NotificationDisplayDuration)
                ? settings.NotificationDisplayDuration
                : ReminderNotificationDisplayDuration.Short;
        var searchHistory = NormalizeSearchHistory(
            settings.SearchHistory);

        var changed = false;
        lock (_gate)
        {
            if (_themeMode != themeMode)
            {
                _themeMode = themeMode;
                changed = true;
            }

            if (_renderingMode != renderingMode)
            {
                _renderingMode = renderingMode;
                changed = true;
            }

            if (_startWithWindows != settings.StartWithWindows)
            {
                _startWithWindows = settings.StartWithWindows;
                changed = true;
            }

            if (_silentStart != settings.SilentStart)
            {
                _silentStart = settings.SilentStart;
                changed = true;
            }

            if (_snoozeDurationMinutes != snoozeDurationMinutes)
            {
                _snoozeDurationMinutes = snoozeDurationMinutes;
                changed = true;
            }

            if (_snoozeOverflowPolicy != snoozeOverflowPolicy)
            {
                _snoozeOverflowPolicy = snoozeOverflowPolicy;
                changed = true;
            }

            if (_notificationDisplayDuration !=
                notificationDisplayDuration)
            {
                _notificationDisplayDuration =
                    notificationDisplayDuration;
                changed = true;
            }

            if (!_searchHistory.SequenceEqual(searchHistory))
            {
                _searchHistory.Clear();
                _searchHistory.AddRange(searchHistory);
                changed = true;
            }
        }

        if (changed)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public bool SetSnoozeDurationMinutes(int minutes)
    {
        if (!IsValidSnoozeDuration(minutes))
        {
            return false;
        }

        lock (_gate)
        {
            if (_snoozeDurationMinutes == minutes)
            {
                return false;
            }

            _snoozeDurationMinutes = minutes;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SetSnoozeOverflowPolicy(
        ReminderSnoozeOverflowPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            return false;
        }

        lock (_gate)
        {
            if (_snoozeOverflowPolicy == policy)
            {
                return false;
            }

            _snoozeOverflowPolicy = policy;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SetNotificationDisplayDuration(
        ReminderNotificationDisplayDuration duration)
    {
        if (!Enum.IsDefined(duration))
        {
            return false;
        }

        lock (_gate)
        {
            if (_notificationDisplayDuration == duration)
            {
                return false;
            }

            _notificationDisplayDuration = duration;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RecordSearchQuery(string? query)
    {
        var normalized = NormalizeSearchQuery(query);
        if (normalized.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            var existingIndex = _searchHistory.FindIndex(item =>
                string.Equals(
                    item,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
            if (existingIndex == 0)
            {
                return false;
            }

            if (existingIndex > 0)
            {
                _searchHistory.RemoveAt(existingIndex);
            }

            _searchHistory.Insert(0, normalized);
            if (_searchHistory.Count > MaximumSearchHistoryCount)
            {
                _searchHistory.RemoveRange(
                    MaximumSearchHistoryCount,
                    _searchHistory.Count - MaximumSearchHistoryCount);
            }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RemoveSearchQuery(string? query)
    {
        var normalized = NormalizeSearchQuery(query);
        bool removed;
        lock (_gate)
        {
            removed = _searchHistory.RemoveAll(item =>
                string.Equals(
                    item,
                    normalized,
                    StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public bool ClearSearchHistory()
    {
        lock (_gate)
        {
            if (_searchHistory.Count == 0)
            {
                return false;
            }

            _searchHistory.Clear();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RestoreDefaults()
    {
        var changed = false;
        lock (_gate)
        {
            if (_themeMode != ReminderThemeMode.FollowSystem)
            {
                _themeMode = ReminderThemeMode.FollowSystem;
                changed = true;
            }

            if (_renderingMode != ReminderRenderingMode.HardwarePreferred)
            {
                _renderingMode = ReminderRenderingMode.HardwarePreferred;
                changed = true;
            }

            if (_startWithWindows)
            {
                _startWithWindows = false;
                changed = true;
            }

            if (_silentStart)
            {
                _silentStart = false;
                changed = true;
            }

            var defaultSnoozeMinutes =
                (int)ReminderDefaults.SnoozeDuration.TotalMinutes;
            if (_snoozeDurationMinutes != defaultSnoozeMinutes)
            {
                _snoozeDurationMinutes = defaultSnoozeMinutes;
                changed = true;
            }

            if (_snoozeOverflowPolicy !=
                ReminderSnoozeOverflowPolicy.ShortenToFixedInterval)
            {
                _snoozeOverflowPolicy =
                    ReminderSnoozeOverflowPolicy.ShortenToFixedInterval;
                changed = true;
            }

            if (_notificationDisplayDuration !=
                ReminderNotificationDisplayDuration.Short)
            {
                _notificationDisplayDuration =
                    ReminderNotificationDisplayDuration.Short;
                changed = true;
            }
        }

        if (changed)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public ReminderApplicationSettings Export()
    {
        lock (_gate)
        {
            return new ReminderApplicationSettings
            {
                ThemeMode = _themeMode,
                RenderingMode = _renderingMode,
                StartWithWindows = _startWithWindows,
                SilentStart = _silentStart,
                SnoozeDurationMinutes = _snoozeDurationMinutes,
                SnoozeOverflowPolicy = _snoozeOverflowPolicy,
                NotificationDisplayDuration =
                    _notificationDisplayDuration,
                SearchHistory = _searchHistory.ToArray()
            };
        }
    }

    public static string NormalizeSearchQuery(string? query)
    {
        return string.Join(
            ' ',
            (query ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
    }

    private static bool IsValidSnoozeDuration(int? minutes)
    {
        return minutes is
            >= ReminderDefaults.MinimumIntervalMinutes and
            <= ReminderDefaults.MaximumIntervalMinutes;
    }

    private static List<string> NormalizeSearchHistory(
        IReadOnlyList<string>? history)
    {
        List<string> result = [];
        foreach (var item in history ?? [])
        {
            var normalized = NormalizeSearchQuery(item);
            if (normalized.Length == 0 ||
                result.Contains(
                    normalized,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(normalized);
            if (result.Count == MaximumSearchHistoryCount)
            {
                break;
            }
        }

        return result;
    }
}

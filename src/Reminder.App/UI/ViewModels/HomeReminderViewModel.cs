using Reminder.App.Logic.Models;

namespace Reminder.App.UI.ViewModels;

public sealed class HomeReminderViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _countdownText = string.Empty;
    private string _statusText = string.Empty;
    private double _remainingSeconds;
    private bool _isAwaitingAction;

    public HomeReminderViewModel(ReminderEventSnapshot snapshot)
    {
        Id = snapshot.Id;
        ApplySnapshot(snapshot);
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public double RemainingSeconds
    {
        get => _remainingSeconds;
        private set => SetProperty(ref _remainingSeconds, value);
    }

    public bool IsAwaitingAction
    {
        get => _isAwaitingAction;
        private set => SetProperty(ref _isAwaitingAction, value);
    }

    public void ApplySnapshot(ReminderEventSnapshot snapshot)
    {
        Name = snapshot.Name;
        IsAwaitingAction = snapshot.IsAwaitingAction;
        RemainingSeconds = Math.Max(
            0,
            snapshot.Remaining?.TotalSeconds ?? 0);
        CountdownText = snapshot.IsAwaitingAction
            ? "等待处理"
            : FormatCountdown(snapshot.Remaining ?? TimeSpan.Zero);
        StatusText = CreateStatusText(snapshot);
    }

    private static string CreateStatusText(ReminderEventSnapshot snapshot)
    {
        if (snapshot.IsAwaitingAction)
        {
            return "等待处理";
        }

        if (snapshot.IsBlockedByGlobalPause)
        {
            if (snapshot.EventType == ReminderEventType.ScheduledTime)
            {
                return snapshot.HasPendingMissedOccurrence ||
                       snapshot.Remaining <= TimeSpan.Zero
                    ? "暂停期间已发生"
                    : "暂停中，到点不提醒";
            }

            return "倒计时已冻结";
        }

        if (snapshot.IsBlockedBySystemState)
        {
            return "当前暂不提醒";
        }

        return snapshot.EventType == ReminderEventType.FixedInterval
            ? "固定间隔事件"
            : "指定时间事件";
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(
            0,
            (long)Math.Ceiling(remaining.TotalSeconds));
        var days = totalSeconds / 86_400;
        var hours = totalSeconds % 86_400 / 3_600;
        var minutes = totalSeconds % 3_600 / 60;
        var seconds = totalSeconds % 60;

        if (days > 0)
        {
            return $"{days}天{hours:00}时{minutes:00}分{seconds:00}秒";
        }

        return hours > 0
            ? $"{hours:00}时{minutes:00}分{seconds:00}秒"
            : $"{minutes:00}分{seconds:00}秒";
    }
}

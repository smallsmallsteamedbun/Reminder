using System.Globalization;
using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;

namespace Reminder.App.UI.ViewModels;

public sealed class EventViewModel : ObservableObject
{
    private readonly ReminderEngine _engine;
    private ReminderEventSnapshot _snapshot;
    private string _nameInput;
    private string _intervalInput;
    private string _nameError = string.Empty;
    private string _intervalError = string.Empty;
    private string _countdownText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isEnabled;
    private bool _isPaused;
    private bool _isAwaitingAction;
    private bool _canRestart;
    private bool _isHighlighted;
    private bool _synchronizing;

    public EventViewModel(
        ReminderEngine engine,
        ReminderEventSnapshot snapshot,
        Action<EventViewModel> deleteRequested)
    {
        _engine = engine;
        _snapshot = snapshot;
        _nameInput = snapshot.Name;
        _intervalInput = snapshot.IntervalMinutes.ToString(CultureInfo.InvariantCulture);

        PauseCommand = new RelayCommand(
            () => _engine.TogglePause(Id),
            () => CanPauseOrResume);
        RestartCommand = new RelayCommand(
            () => _engine.Restart(Id),
            () => CanRestart);
        DeleteCommand = new RelayCommand(() => deleteRequested(this));

        ApplySnapshot(snapshot);
    }

    public Guid Id => _snapshot.Id;

    public RelayCommand PauseCommand { get; }

    public RelayCommand RestartCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetProperty(ref _nameInput, value) && NameError.Length != 0)
            {
                NameError = string.Empty;
            }
        }
    }

    public string IntervalInput
    {
        get => _intervalInput;
        set
        {
            if (SetProperty(ref _intervalInput, value) && IntervalError.Length != 0)
            {
                IntervalError = string.Empty;
            }
        }
    }

    public string NameError
    {
        get => _nameError;
        private set
        {
            if (SetProperty(ref _nameError, value))
            {
                OnPropertyChanged(nameof(HasNameError));
            }
        }
    }

    public bool HasNameError => NameError.Length != 0;

    public string IntervalError
    {
        get => _intervalError;
        private set
        {
            if (SetProperty(ref _intervalError, value))
            {
                OnPropertyChanged(nameof(HasIntervalError));
            }
        }
    }

    public bool HasIntervalError => IntervalError.Length != 0;

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

    public string PauseButtonText => IsPaused ? "恢复" : "暂停";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CardOpacity));
            OnPropertyChanged(nameof(CanPauseOrResume));
            PauseCommand.RaiseCanExecuteChanged();

            if (!_synchronizing)
            {
                _engine.SetEnabled(Id, value);
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(CardOpacity));
            }
        }
    }

    public bool IsAwaitingAction
    {
        get => _isAwaitingAction;
        private set => SetProperty(ref _isAwaitingAction, value);
    }

    public bool CanRestart
    {
        get => _canRestart;
        private set
        {
            if (SetProperty(ref _canRestart, value))
            {
                RestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanPauseOrResume => IsEnabled;

    public bool IsHighlighted
    {
        get => _isHighlighted;
        private set => SetProperty(ref _isHighlighted, value);
    }

    public double CardOpacity => IsEnabled && !IsPaused ? 1.0 : 0.64;

    public void SetHighlighted(bool isHighlighted)
    {
        IsHighlighted = isHighlighted;
    }

    public void CommitName()
    {
        if (!ReminderInputValidator.TryValidateName(NameInput, out var value, out var error))
        {
            NameError = error;
            return;
        }

        if (!_engine.UpdateName(Id, value))
        {
            NameError = "无法应用事件名称";
            return;
        }

        NameError = string.Empty;
        NameInput = value;
    }

    public void CommitInterval()
    {
        if (!ReminderInputValidator.TryValidateInterval(
                IntervalInput,
                out var value,
                out var error))
        {
            IntervalError = error;
            return;
        }

        if (!_engine.UpdateInterval(Id, value))
        {
            IntervalError = "无法应用提醒间隔";
            return;
        }

        IntervalError = string.Empty;
        IntervalInput = value.ToString(CultureInfo.InvariantCulture);
    }

    public void ApplySnapshot(ReminderEventSnapshot snapshot)
    {
        var nameInputWasUnmodified = NameInput == _snapshot.Name;
        var intervalInputWasUnmodified =
            IntervalInput == _snapshot.IntervalMinutes.ToString(CultureInfo.InvariantCulture);

        _snapshot = snapshot;
        _synchronizing = true;
        try
        {
            IsEnabled = snapshot.IsEnabled;
            IsPaused = snapshot.IsPaused;
            IsAwaitingAction = snapshot.IsAwaitingAction;
            CanRestart = snapshot.CanRestart;

            if (nameInputWasUnmodified)
            {
                NameInput = snapshot.Name;
            }

            if (intervalInputWasUnmodified)
            {
                IntervalInput = snapshot.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _synchronizing = false;
        }

        CountdownText = snapshot.IsAwaitingAction
            ? "等待处理"
            : FormatCountdown(snapshot.Remaining);

        StatusText = !snapshot.IsEnabled
            ? "已关闭"
            : snapshot.IsPaused
                ? "已暂停"
                : snapshot.IsAwaitingAction
                    ? "等待处理"
                    : "运行中";

        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(CanPauseOrResume));
        PauseCommand.RaiseCanExecuteChanged();
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        var days = totalSeconds / 86_400;
        var hours = totalSeconds % 86_400 / 3_600;
        var minutes = totalSeconds % 3_600 / 60;
        var seconds = totalSeconds % 60;

        if (days > 0)
        {
            return $"{days}天{hours:00}时{minutes:00}分{seconds:00}秒";
        }

        if (hours > 0)
        {
            return $"{hours:00}时{minutes:00}分{seconds:00}秒";
        }

        return $"{minutes:00}分{seconds:00}秒";
    }
}

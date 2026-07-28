namespace Reminder.App.SystemModule.Persistence;

public enum ReminderStateLoadStatus
{
    NoData,
    LoadedPrimary,
    LoadedBackup,
    RecoveryFailed
}

public sealed record ReminderStateLoadResult
{
    public required ReminderStateLoadStatus Status { get; init; }

    public required ReminderPersistedState? State { get; init; }

    public required string ErrorMessage { get; init; }

    public bool HasState =>
        Status is ReminderStateLoadStatus.LoadedPrimary or
            ReminderStateLoadStatus.LoadedBackup;
}

public sealed record ReminderStateSaveResult
{
    public required bool IsSuccess { get; init; }

    public required string ErrorMessage { get; init; }

    public static ReminderStateSaveResult Success { get; } = new()
    {
        IsSuccess = true,
        ErrorMessage = string.Empty
    };
}

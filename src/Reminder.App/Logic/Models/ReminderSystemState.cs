namespace Reminder.App.Logic.Models;

public readonly record struct ReminderSystemState(
    bool IsSessionLocked,
    bool IsDisplayOff,
    bool IsSleeping)
{
    public bool IsScreenOrLockUnavailable =>
        IsSessionLocked || IsDisplayOff;
}

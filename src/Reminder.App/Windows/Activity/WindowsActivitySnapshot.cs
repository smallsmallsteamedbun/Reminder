namespace Reminder.App.Windows.Activity;

public readonly record struct WindowsActivitySnapshot(
    bool IsSessionLocked,
    bool IsDisplayOff,
    bool IsSleeping);

public sealed class WindowsActivityChangedEventArgs(
    WindowsActivitySnapshot snapshot,
    DateTimeOffset occurredAt) : EventArgs
{
    public WindowsActivitySnapshot Snapshot { get; } = snapshot;

    public DateTimeOffset OccurredAt { get; } = occurredAt;
}

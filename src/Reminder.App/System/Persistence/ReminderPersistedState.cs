using Reminder.App.Logic.State;
using Reminder.App.SystemModule.Settings;

namespace Reminder.App.SystemModule.Persistence;

public sealed record ReminderPersistedState
{
    public required ReminderEngineState EngineState { get; init; }

    public required ReminderApplicationSettings Settings { get; init; }

    public DateTimeOffset SavedAt => EngineState.SavedAt;

    public IReadOnlyList<ReminderEngineEventState> Events =>
        EngineState.Events;

    public ReminderEngineGlobalPauseState GlobalPause =>
        EngineState.GlobalPause;

    public IReadOnlyList<Guid> PendingMissedEventIds =>
        EngineState.PendingMissedEventIds;

    public static implicit operator ReminderEngineState(
        ReminderPersistedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.EngineState;
    }
}

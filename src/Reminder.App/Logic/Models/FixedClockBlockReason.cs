namespace Reminder.App.Logic.Models;

[Flags]
internal enum FixedClockBlockReason
{
    None = 0,
    GlobalPause = 1,
    SystemUnavailable = 2
}

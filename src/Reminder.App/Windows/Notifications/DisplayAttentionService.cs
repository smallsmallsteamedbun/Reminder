using System.Runtime.InteropServices;

namespace Reminder.App.Windows.Notifications;

internal static class DisplayAttentionService
{
    private const uint EsDisplayRequired = 0x00000002;

    public static bool TryRequestDisplayOn()
    {
        return SetThreadExecutionState(EsDisplayRequired) != 0;
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}

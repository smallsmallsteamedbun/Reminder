using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Reminder.App.SystemModule.Runtime;

public static class ProcessMemoryTrimmer
{
    public static void TrimAfterWindowHidden()
    {
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Optimized,
            blocking: false,
            compacting: false);

        using var process = Process.GetCurrentProcess();
        EmptyWorkingSet(process.Handle);
    }

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint processHandle);
}

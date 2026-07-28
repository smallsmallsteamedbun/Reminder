using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Reminder.App.SystemModule.Runtime;

public static class ReminderProcessRestarter
{
    private static readonly TimeSpan PreviousProcessWaitTimeout =
        TimeSpan.FromMinutes(1);

    public static bool WaitForPreviousProcessIfRequested(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    "--wait-for-pid",
                    StringComparison.Ordinal) ||
                !int.TryParse(
                    arguments[index + 1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var processId))
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                return process.WaitForExit(
                    checked((int)PreviousProcessWaitTimeout.TotalMilliseconds));
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        return true;
    }

    public static bool TryStartReplacementProcess()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var entryAssemblyPath =
                    Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(entryAssemblyPath))
                {
                    return false;
                }

                startInfo.ArgumentList.Add(entryAssemblyPath);
            }

            startInfo.ArgumentList.Add("--wait-for-pid");
            startInfo.ArgumentList.Add(
                Environment.ProcessId.ToString(
                    CultureInfo.InvariantCulture));
            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }
}

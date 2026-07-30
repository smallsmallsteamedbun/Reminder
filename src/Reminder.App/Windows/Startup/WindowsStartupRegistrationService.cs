using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;

namespace Reminder.App.Windows.Startup;

public sealed class WindowsStartupRegistrationService :
    IWindowsStartupRegistrationService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Reminder";
    private readonly string _launchCommand;

    public WindowsStartupRegistrationService()
        : this(BuildLaunchCommand())
    {
    }

    internal WindowsStartupRegistrationService(string launchCommand)
    {
        if (string.IsNullOrWhiteSpace(launchCommand))
        {
            throw new ArgumentException(
                "开机启动命令不能为空。",
                nameof(launchCommand));
        }

        _launchCommand = launchCommand;
    }

    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    RunKeyPath,
                    writable: false);
                var current = key?.GetValue(ValueName) as string;
                return string.Equals(
                    current,
                    _launchCommand,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is SecurityException or
                UnauthorizedAccessException or
                IOException)
            {
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled, out string errorMessage)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true);
            if (key is null)
            {
                errorMessage = "无法访问 Windows 当前用户启动项。";
                return false;
            }

            if (enabled)
            {
                key.SetValue(
                    ValueName,
                    _launchCommand,
                    RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(
                    ValueName,
                    throwOnMissingValue: false);
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is SecurityException or
            UnauthorizedAccessException or
            IOException or
            Win32Exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    internal static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException(
                "无法确定 Reminder 的运行路径。");
        }

        if (!string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{Quote(processPath)} --startup";
        }

        var entryAssemblyPath =
            Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            throw new InvalidOperationException(
                "无法确定 Reminder 程序集路径。");
        }

        return $"{Quote(processPath)} {Quote(entryAssemblyPath)} --startup";
    }

    private static string Quote(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "启动路径包含不受支持的引号字符。");
        }

        return $"\"{value}\"";
    }
}

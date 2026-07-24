using System.Globalization;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.Logic.Services;

public static class ReminderInputValidator
{
    public static bool TryValidateName(string? input, out string value, out string error)
    {
        value = input?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            error = "事件名称不能为空";
            return false;
        }

        if (value.Length > ReminderDefaults.MaximumEventNameLength)
        {
            error = $"事件名称最多 {ReminderDefaults.MaximumEventNameLength} 个字符";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateInterval(string? input, out int value, out string error)
    {
        if (!int.TryParse(
                input,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value))
        {
            error = "请输入整数分钟";
            return false;
        }

        if (value is < ReminderDefaults.MinimumIntervalMinutes or > ReminderDefaults.MaximumIntervalMinutes)
        {
            error =
                $"请输入 {ReminderDefaults.MinimumIntervalMinutes}–{ReminderDefaults.MaximumIntervalMinutes} 分钟";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

using System.Globalization;
using System.Text;
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

        if (!IsNameWithinMaximumLength(value))
        {
            error = "事件名称最多 25 个中文字符或 50 个英文字符";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsNameWithinMaximumLength(string? input)
    {
        return GetNameWeightedLength(input) <=
               ReminderDefaults.MaximumEventNameLength;
    }

    public static int GetNameWeightedLength(string? input)
    {
        return (input ?? string.Empty).EnumerateRunes().Sum(
            rune => rune.IsAscii ? 1 : 2);
    }

    public static bool TryValidateInterval(string? input, out int value, out string error)
    {
        return TryValidateIntervalParts(
            "0",
            "0",
            input,
            out value,
            out error);
    }

    public static bool TryValidateIntervalParts(
        string? daysInput,
        string? hoursInput,
        string? minutesInput,
        out int totalMinutes,
        out string error)
    {
        totalMinutes = 0;
        if (!TryParseIntervalPart(
                daysInput,
                maximumLength: 3,
                out var days) ||
            !TryParseIntervalPart(
                hoursInput,
                maximumLength: 4,
                out var hours) ||
            !TryParseIntervalPart(
                minutesInput,
                maximumLength: 6,
                out var minutes))
        {
            error = "天、小时和分钟请输入非负整数";
            return false;
        }

        long calculatedMinutes;
        try
        {
            calculatedMinutes = checked(
                days * 24L * 60L +
                hours * 60L +
                minutes);
        }
        catch (OverflowException)
        {
            error = "提醒间隔不能超过 365 天";
            return false;
        }

        if (calculatedMinutes < ReminderDefaults.MinimumIntervalMinutes)
        {
            error = "提醒间隔至少为 1 分钟";
            return false;
        }

        if (calculatedMinutes > ReminderDefaults.MaximumIntervalMinutes)
        {
            error = "提醒间隔不能超过 365 天";
            return false;
        }

        totalMinutes = (int)calculatedMinutes;
        error = string.Empty;
        return true;
    }

    public static bool TryValidateOneTime(
        string? input,
        out DateTimeOffset value,
        out string error)
    {
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm",
            "yyyy-M-d H:mm",
            "yyyy/M/d H:mm",
            "yyyy/M/d HH:mm"
        };

        if (!DateTime.TryParseExact(
                input?.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localTime))
        {
            value = default;
            error = "请输入有效时间，例如 2026-07-24 18:30";
            return false;
        }

        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localTime))
        {
            value = default;
            error = "该时间因系统时区切换而不存在，请选择其他时间";
            return false;
        }

        value = new DateTimeOffset(
            localTime,
            TimeZoneInfo.Local.GetUtcOffset(localTime));
        error = string.Empty;
        return true;
    }

    public static bool TryValidateTimeOfDay(
        string? input,
        out TimeOnly value,
        out string error)
    {
        var formats = new[] { "HH:mm", "H:mm" };
        if (!TimeOnly.TryParseExact(
                input?.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out value))
        {
            error = "请输入有效时间，例如 08:30";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateDayOfMonth(
        string? input,
        out int value,
        out string error)
    {
        if (!int.TryParse(
                input,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) ||
            value is < 1 or > 31)
        {
            error = "日期请输入 1–31";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateMonth(
        string? input,
        out int value,
        out string error)
    {
        if (!int.TryParse(
                input,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) ||
            value is < 1 or > 12)
        {
            error = "月份请输入 1–12";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateYear(
        string? input,
        out int value,
        out string error)
    {
        if (!int.TryParse(
                input,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) ||
            value is < 1 or > 9_999)
        {
            error = "年份请输入 1–9999";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateHour(
        string? input,
        out int value,
        out string error)
    {
        if (!int.TryParse(
                input,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) ||
            value is < 0 or > 23)
        {
            error = "小时请输入 0–23";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateMinute(
        string? input,
        out int value,
        out string error)
    {
        if (!int.TryParse(
                input,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) ||
            value is < 0 or > 59)
        {
            error = "分钟请输入 0–59";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryCreateLocalDateTime(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        out DateTimeOffset value,
        out string error)
    {
        if (year is < 1 or > 9_999 ||
            month is < 1 or > 12 ||
            day < 1 ||
            day > DateTime.DaysInMonth(year, month) ||
            hour is < 0 or > 23 ||
            minute is < 0 or > 59)
        {
            value = default;
            error = "请输入真实存在的日期和时间";
            return false;
        }

        var localTime = new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            0,
            DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localTime))
        {
            value = default;
            error = "该时间因系统时区切换而不存在，请选择其他时间";
            return false;
        }

        value = new DateTimeOffset(
            localTime,
            TimeZoneInfo.Local.GetUtcOffset(localTime));
        error = string.Empty;
        return true;
    }

    private static bool TryParseIntervalPart(
        string? input,
        int maximumLength,
        out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(input))
        {
            return true;
        }

        if (input.Length > maximumLength ||
            input.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        return long.TryParse(
            input,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }
}

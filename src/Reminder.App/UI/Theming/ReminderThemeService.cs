using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Reminder.App.SystemModule.Settings;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Reminder.App.UI.Theming;

public sealed class ReminderThemeService : IDisposable
{
    private static readonly ThemePalette LightPalette = new(
        PageBackground: Color.FromRgb(0xF3, 0xF5, 0xF8),
        Surface: Colors.White,
        SurfaceSubtle: Color.FromRgb(0xF7, 0xF8, 0xFB),
        Border: Color.FromRgb(0xE3, 0xE6, 0xEC),
        BorderStrong: Color.FromRgb(0xCD, 0xD2, 0xDC),
        TextPrimary: Color.FromRgb(0x20, 0x23, 0x2B),
        TextSecondary: Color.FromRgb(0x6D, 0x72, 0x80),
        TextMuted: Color.FromRgb(0x98, 0x9E, 0xAA),
        Primary: Color.FromRgb(0x5F, 0x68, 0xC7),
        PrimaryHover: Color.FromRgb(0x53, 0x5D, 0xBD),
        PrimaryPressed: Color.FromRgb(0x48, 0x52, 0xAE),
        PrimarySubtle: Color.FromRgb(0xEC, 0xEE, 0xFB),
        PrimarySelection: Color.FromRgb(0xE3, 0xE6, 0xFA),
        Danger: Color.FromRgb(0xC4, 0x4D, 0x58),
        DangerHover: Color.FromRgb(0xB5, 0x40, 0x4C),
        DangerPressed: Color.FromRgb(0x9F, 0x35, 0x41),
        DangerSubtle: Color.FromRgb(0xFB, 0xEC, 0xEF),
        Success: Color.FromRgb(0x3F, 0xAE, 0x78),
        TextOnAccent: Colors.White,
        SwitchThumb: Colors.White,
        PopupShadow: Color.FromArgb(0x16, 0x0F, 0x17, 0x2A),
        CardShadow: Color.FromArgb(0x14, 0x0F, 0x17, 0x2A),
        CircleShadow: Color.FromArgb(0x12, 0x0F, 0x17, 0x2A),
        DialogShadow: Color.FromArgb(0x42, 0x00, 0x00, 0x00));

    private static readonly ThemePalette DarkPalette = new(
        PageBackground: Color.FromRgb(0x17, 0x19, 0x1F),
        Surface: Color.FromRgb(0x20, 0x23, 0x2A),
        SurfaceSubtle: Color.FromRgb(0x27, 0x2A, 0x32),
        Border: Color.FromRgb(0x34, 0x38, 0x43),
        BorderStrong: Color.FromRgb(0x47, 0x4C, 0x59),
        TextPrimary: Color.FromRgb(0xF1, 0xF3, 0xF7),
        TextSecondary: Color.FromRgb(0xB8, 0xBD, 0xC9),
        TextMuted: Color.FromRgb(0x85, 0x8B, 0x99),
        Primary: Color.FromRgb(0x8B, 0x93, 0xEE),
        PrimaryHover: Color.FromRgb(0x9A, 0xA1, 0xF5),
        PrimaryPressed: Color.FromRgb(0x74, 0x7D, 0xE1),
        PrimarySubtle: Color.FromRgb(0x30, 0x34, 0x4C),
        PrimarySelection: Color.FromRgb(0x3A, 0x3F, 0x63),
        Danger: Color.FromRgb(0xE1, 0x6B, 0x77),
        DangerHover: Color.FromRgb(0xEB, 0x7B, 0x87),
        DangerPressed: Color.FromRgb(0xCC, 0x58, 0x64),
        DangerSubtle: Color.FromRgb(0x44, 0x28, 0x2E),
        Success: Color.FromRgb(0x62, 0xD6, 0x9A),
        TextOnAccent: Color.FromRgb(0x12, 0x14, 0x1A),
        SwitchThumb: Colors.White,
        PopupShadow: Color.FromArgb(0x5A, 0x00, 0x00, 0x00),
        CardShadow: Color.FromArgb(0x52, 0x00, 0x00, 0x00),
        CircleShadow: Color.FromArgb(0x4A, 0x00, 0x00, 0x00),
        DialogShadow: Color.FromArgb(0x88, 0x00, 0x00, 0x00));

    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly ReminderApplicationSettingsService _settings;
    private readonly Application _application;
    private readonly ResourceDictionary _themeResources = new();
    private ReminderThemeMode _appliedMode;
    private bool _isDark;
    private bool _hasApplied;
    private bool _disposed;

    public ReminderThemeService(
        Application application,
        ReminderApplicationSettingsService settings)
    {
        _application = application;
        _settings = settings;
        _settings.SettingsChanged += OnSettingsChanged;
        SystemEvents.UserPreferenceChanged +=
            OnSystemUserPreferenceChanged;
        EnsureThemeResourcesAttached();
        ApplyCurrentTheme(force: true);
    }

    public event EventHandler? ThemeChanged;

    public ReminderThemeMode ThemeMode => _settings.ThemeMode;

    public bool IsDark => _isDark;

    public void Reapply()
    {
        RunOnDispatcher(() => ApplyCurrentTheme(force: true));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
        SystemEvents.UserPreferenceChanged -=
            OnSystemUserPreferenceChanged;
        _application.Resources.MergedDictionaries.Remove(
            _themeResources);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        RunOnDispatcher(() => ApplyCurrentTheme(force: false));
    }

    private void OnSystemUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (_settings.ThemeMode == ReminderThemeMode.FollowSystem)
        {
            RunOnDispatcher(() => ApplyCurrentTheme(force: false));
        }
    }

    private void RunOnDispatcher(Action action)
    {
        var dispatcher = _application.Dispatcher;
        if (dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private void ApplyCurrentTheme(bool force)
    {
        if (_disposed)
        {
            return;
        }

        EnsureThemeResourcesAttached();
        var mode = _settings.ThemeMode;
        var isDark = mode switch
        {
            ReminderThemeMode.Dark => true,
            ReminderThemeMode.Light => false,
            _ => IsSystemAppThemeDark()
        };
        if (!force &&
            _hasApplied &&
            _appliedMode == mode &&
            _isDark == isDark)
        {
            return;
        }

        ApplyPalette(
            _themeResources,
            isDark ? DarkPalette : LightPalette);
        _appliedMode = mode;
        _isDark = isDark;
        _hasApplied = true;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsSystemAppThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                PersonalizeKeyPath,
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value &&
                   value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyPalette(
        ResourceDictionary resources,
        ThemePalette palette)
    {
        SetColorAndBrush(
            resources,
            "PageBackground",
            palette.PageBackground);
        SetColorAndBrush(resources, "Surface", palette.Surface);
        SetColorAndBrush(
            resources,
            "SurfaceSubtle",
            palette.SurfaceSubtle);
        SetColorAndBrush(resources, "Border", palette.Border);
        SetColorAndBrush(
            resources,
            "BorderStrong",
            palette.BorderStrong);
        SetColorAndBrush(
            resources,
            "TextPrimary",
            palette.TextPrimary);
        SetColorAndBrush(
            resources,
            "TextSecondary",
            palette.TextSecondary);
        SetColorAndBrush(
            resources,
            "TextMuted",
            palette.TextMuted);
        SetColorAndBrush(resources, "Primary", palette.Primary);
        SetColorAndBrush(
            resources,
            "PrimaryHover",
            palette.PrimaryHover);
        SetColorAndBrush(
            resources,
            "PrimaryPressed",
            palette.PrimaryPressed);
        SetColorAndBrush(
            resources,
            "PrimarySubtle",
            palette.PrimarySubtle);
        SetColorAndBrush(
            resources,
            "PrimarySelection",
            palette.PrimarySelection);
        SetColorAndBrush(resources, "Danger", palette.Danger);
        SetColorAndBrush(
            resources,
            "DangerHover",
            palette.DangerHover);
        SetColorAndBrush(
            resources,
            "DangerPressed",
            palette.DangerPressed);
        SetColorAndBrush(
            resources,
            "DangerSubtle",
            palette.DangerSubtle);
        SetColorAndBrush(
            resources,
            "Success",
            palette.Success);
        SetColorAndBrush(
            resources,
            "TextOnAccent",
            palette.TextOnAccent);
        SetColorAndBrush(
            resources,
            "SwitchThumb",
            palette.SwitchThumb);
        SetColorAndBrush(
            resources,
            "PopupShadow",
            palette.PopupShadow);
        SetColorAndBrush(
            resources,
            "CardShadow",
            palette.CardShadow);
        SetColorAndBrush(
            resources,
            "CircleShadow",
            palette.CircleShadow);
        resources["DialogShadowColor"] = palette.DialogShadow;
    }

    private void EnsureThemeResourcesAttached()
    {
        var mergedDictionaries =
            _application.Resources.MergedDictionaries;
        if (mergedDictionaries.Contains(_themeResources))
        {
            return;
        }

        mergedDictionaries.Add(_themeResources);
    }

    private static void SetColorAndBrush(
        ResourceDictionary resources,
        string keyPrefix,
        Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[$"{keyPrefix}Color"] = color;
        resources[$"{keyPrefix}Brush"] = brush;
    }

    private sealed record ThemePalette(
        Color PageBackground,
        Color Surface,
        Color SurfaceSubtle,
        Color Border,
        Color BorderStrong,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color Primary,
        Color PrimaryHover,
        Color PrimaryPressed,
        Color PrimarySubtle,
        Color PrimarySelection,
        Color Danger,
        Color DangerHover,
        Color DangerPressed,
        Color DangerSubtle,
        Color Success,
        Color TextOnAccent,
        Color SwitchThumb,
        Color PopupShadow,
        Color CardShadow,
        Color CircleShadow,
        Color DialogShadow);
}

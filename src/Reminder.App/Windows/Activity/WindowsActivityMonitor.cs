using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Reminder.App.Windows.Activity;

public sealed class WindowsActivityMonitor : IWindowsActivityMonitor
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private const int DeviceNotifyWindowHandle = 0;
    private const int PowerBroadcastSettingDataOffset = 20;
    private const uint DesktopSwitchDesktop = 0x0100;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly Guid SessionDisplayStatusGuid =
        new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

    private readonly object _gate = new();
    private readonly HwndSource _messageSource;
    private WindowsActivitySnapshot _snapshot;
    private IntPtr _displayStatusRegistration;
    private bool _disposed;

    public WindowsActivityMonitor()
    {
        _snapshot = new WindowsActivitySnapshot(
            IsSessionLockedAtStartup(),
            IsDisplayOff: false,
            IsSleeping: false);

        var parameters =
            new HwndSourceParameters("Reminder.WindowsActivityMonitor")
            {
                ParentWindow = HwndMessage,
                WindowStyle = 0,
                Width = 0,
                Height = 0
            };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WindowMessageHook);

        var displayStatusGuid = SessionDisplayStatusGuid;
        _displayStatusRegistration =
            RegisterPowerSettingNotification(
                _messageSource.Handle,
                ref displayStatusGuid,
                DeviceNotifyWindowHandle);

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler<WindowsActivityChangedEventArgs>? StateChanged;

    public WindowsActivitySnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_displayStatusRegistration != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(
                _displayStatusRegistration);
            _displayStatusRegistration = IntPtr.Zero;
        }

        _messageSource.RemoveHook(WindowMessageHook);
        _messageSource.Dispose();
    }

    private void OnSessionSwitch(
        object sender,
        SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Update(snapshot => snapshot with
                {
                    IsSessionLocked = true
                });
                break;
            case SessionSwitchReason.SessionUnlock:
                Update(snapshot => snapshot with
                {
                    IsSessionLocked = false
                });
                break;
        }
    }

    private void OnPowerModeChanged(
        object sender,
        PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Update(snapshot => snapshot with
                {
                    IsSleeping = true
                });
                break;
            case PowerModes.Resume:
                Update(snapshot => snapshot with
                {
                    IsSleeping = false
                });
                break;
        }
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmPowerBroadcast ||
            wParam.ToInt32() != PbtPowerSettingChange ||
            lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var settingGuid = Marshal.PtrToStructure<Guid>(lParam);
        if (settingGuid != SessionDisplayStatusGuid)
        {
            return IntPtr.Zero;
        }

        var dataLength = Marshal.ReadInt32(lParam, 16);
        if (dataLength < 1)
        {
            return IntPtr.Zero;
        }

        var displayStatus = Marshal.ReadByte(
            lParam,
            PowerBroadcastSettingDataOffset);
        Update(snapshot => snapshot with
        {
            IsDisplayOff = displayStatus == 0
        });
        return IntPtr.Zero;
    }

    private void Update(
        Func<WindowsActivitySnapshot, WindowsActivitySnapshot> update)
    {
        WindowsActivitySnapshot next;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            next = update(_snapshot);
            if (next == _snapshot)
            {
                return;
            }

            _snapshot = next;
        }

        StateChanged?.Invoke(
            this,
            new WindowsActivityChangedEventArgs(
                next,
                DateTimeOffset.Now));
    }

    private static bool IsSessionLockedAtStartup()
    {
        var desktop = OpenInputDesktop(
            flags: 0,
            inherit: false,
            desiredAccess: DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            return !SwitchDesktop(desktop);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(
        IntPtr recipient,
        ref Guid powerSettingGuid,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(
        IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SwitchDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
}

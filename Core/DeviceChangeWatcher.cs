using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DS5Bridge.Core;

/// <summary>Fires when any device interface arrives or is removed, via a hidden
/// window receiving WM_DEVICECHANGE. This lets us re-apply hiding within
/// milliseconds of a replug, closing the window where a freshly enumerated
/// GameSir node is briefly visible to a running game (which is what makes the
/// pad "come back" and clash with the virtual DualSense).
///
/// Uses a WinForms <see cref="NativeWindow"/> so class registration and the
/// message pump are handled by WinForms; must be constructed on a thread with a
/// running message loop (the UI thread, created from the tray app).</summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public char dbcc_name;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private sealed class Sink : NativeWindow
    {
        public event Action? Changed;
        public Sink() => CreateHandle(new CreateParams { Caption = "DS5BridgeDevNotify" });
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int evt = m.WParam.ToInt32();
                if (evt == DBT_DEVICEARRIVAL || evt == DBT_DEVICEREMOVECOMPLETE)
                {
                    try { Changed?.Invoke(); } catch { }
                }
            }
            base.WndProc(ref m);
        }
    }

    public event Action? Changed;
    public bool IsActive { get; private set; }
    public string Diagnostic { get; private set; } = "";

    private readonly Sink _sink;
    private IntPtr _notify;

    public DeviceChangeWatcher()
    {
        _sink = new Sink();
        if (_sink.Handle == IntPtr.Zero)
        {
            Diagnostic = "NativeWindow handle is null";
            return;
        }
        _sink.Changed += () => Changed?.Invoke();

        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = 0x00000005,   // DBT_DEVTYP_DEVICEINTERFACE
        };
        var buf = Marshal.AllocHGlobal(filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buf, false);
            _notify = RegisterDeviceNotification(_sink.Handle, buf, DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);
            if (_notify == IntPtr.Zero)
                Diagnostic = $"RegisterDeviceNotification failed err={Marshal.GetLastWin32Error()}";
            else
                IsActive = true;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose()
    {
        if (_notify != IntPtr.Zero) { UnregisterDeviceNotification(_notify); _notify = IntPtr.Zero; }
        _sink.DestroyHandle();
    }
}

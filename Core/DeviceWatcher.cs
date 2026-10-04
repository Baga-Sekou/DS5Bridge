using System.Management;

namespace DS5Bridge.Core;

public enum GameSirMode
{
    None,
    Wired,     // direct USB, PID 0x1050
    Wireless,  // through the GameSir-Dongle receiver, PID 0x103E
}

public sealed record GameSirPresence(GameSirMode Mode, string? InstanceId)
{
    public bool Connected => Mode != GameSirMode.None;
}

/// <summary>Watches for the GameSir (九尾狐) controller and reports whether it
/// is connected and how (wired direct vs through the dongle). Detection is
/// purely by USB PID under GameSir's vendor id 0x3537 — the friendly name is
/// identical ("Xbox 360 Controller for Windows") in both cases, so the name
/// cannot be used.</summary>
public sealed class DeviceWatcher : IDisposable
{
    public const ushort VendorId = 0x3537;
    public const ushort WiredPid = 0x1050;
    public const ushort DonglePid = 0x103E;

    private readonly System.Threading.Timer _timer;
    private GameSirPresence _last = new(GameSirMode.None, null);

    /// <summary>Raised when the detected mode changes (including to/from None).</summary>
    public event Action<GameSirPresence>? Changed;

    public GameSirPresence Current { get; private set; } = new(GameSirMode.None, null);

    public DeviceWatcher()
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, 800);
    }

    private void Poll()
    {
        var presence = Scan();
        if (presence != _last)
        {
            _last = presence;
            Current = presence;
            Changed?.Invoke(presence);
        }
    }

    private static GameSirPresence Scan()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_3537%'");
            ushort? wiredPid = null;
            ushort? donglePid = null;

            foreach (var obj in searcher.Get())
            {
                var id = obj["DeviceID"] as string;
                if (string.IsNullOrEmpty(id)) continue;

                var pid = ParsePid(id);
                if (pid == WiredPid) wiredPid = WiredPid;
                else if (pid == DonglePid) donglePid = DonglePid;
            }

            if (wiredPid is not null) return new GameSirPresence(GameSirMode.Wired, $"VID_3537&PID_{WiredPid:X4}");
            if (donglePid is not null) return new GameSirPresence(GameSirMode.Wireless, $"VID_3537&PID_{DonglePid:X4}");
            return new GameSirPresence(GameSirMode.None, null);
        }
        catch
        {
            return new GameSirPresence(GameSirMode.None, null);
        }
    }

    private static ushort? ParsePid(string instanceId)
    {
        int idx = instanceId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || idx + 8 > instanceId.Length) return null;
        var hex = instanceId.Substring(idx + 4, 4);
        return ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }

    public void Dispose() => _timer.Dispose();
}

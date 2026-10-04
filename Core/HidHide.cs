using System.Diagnostics;
using System.Management;

namespace DS5Bridge.Core;

/// <summary>Wraps the HidHide CLI to hide the physical GameSir pad from games, so
/// only the virtual DualSense is visible.
///
/// HidHide's cloaking is a GLOBAL switch, and other tools (DSX / DualSenseX)
/// already rely on it — this machine has DS5 devices hidden by DSX. So we never
/// toggle it blindly: we remember the prior cloak state and restore it, and we
/// only add/remove OUR GameSir entries (never touch anyone else's). Net effect:
/// hiding is fully reversible, so stopping the bridge returns the pad's XInput
/// signal to normal.</summary>
public sealed class HidHide
{
    private readonly List<string> _hiddenByUs = new();
    private bool _prevCloak;
    private bool _applied;
    private string? _lastError;
    private System.Threading.Timer? _refreshTimer;
    private readonly object _lock = new();

    /// <summary>Non-null if the last Enable/Disable could not complete cleanly.</summary>
    public string? LastError => _lastError;

    /// <summary>True while this instance has hiding applied.</summary>
    public bool IsApplied => _applied;

    public string CliPath { get; }
    public bool Available => File.Exists(CliPath);

    public HidHide(string? overridePath = null)
    {
        CliPath = overridePath ?? FindCli() ?? @"C:\Program Files\HidHide\HidHideCLI.exe";
    }

    /// <summary>Locate HidHideCLI.exe: from the watchdog service path first, then
    /// the usual install locations.</summary>
    public static string? FindCli()
    {
        try
        {
            using var q = new ManagementObjectSearcher(
                "SELECT PathName FROM Win32_Service WHERE Name='HidHideWatchdog'");
            foreach (var o in q.Get())
            {
                var p = o["PathName"] as string;
                if (!string.IsNullOrEmpty(p))
                {
                    var dir = Path.GetDirectoryName(p.Trim('"'));
                    if (dir is not null)
                    {
                        var cli = Path.Combine(dir, "HidHideCLI.exe");
                        if (File.Exists(cli)) return cli;
                    }
                }
            }
        }
        catch { /* fall through */ }

        foreach (var cand in new[]
        {
            @"E:\HidHide\x64\HidHideCLI.exe",
            @"C:\Program Files\HidHide\HidHideCLI.exe",
            @"C:\Program Files (x86)\HidHide\HidHideCLI.exe",
        })
            if (File.Exists(cand)) return cand;

        return null;
    }

    /// <summary>Present device instance paths belonging to the GameSir pad
    /// (both wired 0x1050 and dongle 0x103E).
    ///
    /// CRITICAL: this must include the USB-class nodes, not just HID-class ones.
    /// The pad exposes its XInput interface as a USB node in the XnaComposite
    /// class (USB\VID_3537&PID_1050\...); hiding only HID\VID_3537* leaves the
    /// pad visible to games over XInput, which shows up as "two controllers" /
    /// signal clash. Matching every VID_3537 node hides both faces at once.</summary>
    public static string[] GameSirHidPaths()
    {
        var list = new List<string>();
        try
        {
            using var q = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_3537%'");
            foreach (var o in q.Get())
            {
                var id = o["DeviceID"] as string;
                if (!string.IsNullOrEmpty(id)) list.Add(id);
            }
        }
        catch { /* ignore */ }
        return list.ToArray();
    }

    /// <summary>True if the HidHide control device answers. When false, cloaking
    /// is already active/locked and NOTHING can change it without a reboot —
    /// do not attempt to hide in that state.</summary>
    public bool IsReachable() => CanTalkToDriver();

    public void Enable()
    {
        if (_applied) return;

        // PREFLIGHT. If we cannot talk to the driver now, cloaking is already
        // on/locked (a prior crash or another tool left it stuck) and enabling
        // would only deepen the trap. Refuse and report.
        if (!CanTalkToDriver())
        {
            _lastError = "HidHide 控制设备无响应（cloaking 可能已锁定，需重启）";
            Debug.WriteLine("HidHide: preflight failed — control device unreachable");
            return;
        }

        _prevCloak = ReadCloakState();

        // Register ourselves AND the CLI in the whitelist BEFORE cloaking.
        // A process not on the list is locked out of the HidHide control device
        // once cloaking is on — including HidHideCLI.exe itself, which would
        // strand the machine with hiding stuck on until a reboot.
        if (Environment.ProcessPath is { } exe)
            Run($"--app-reg \"{exe}\"");
        Run($"--app-reg \"{CliPath}\"");

        var hidden = GameSirHidPaths();
        foreach (var path in hidden)
        {
            Run($"--dev-hide \"{path}\"");
            _hiddenByUs.Add(path);
        }
        Run("--cloak-on");

        // POSTFLIGHT. If the control device went unreachable right after
        // cloak-on, we are in the one-way trap: hiding is active and cannot be
        // undone by software. Best-effort revert, then report loudly.
        if (!CanTalkToDriver())
        {
            Debug.WriteLine("HidHide: control device unreachable after cloak-on; attempting revert");
            Run("--cloak-off");
            foreach (var path in _hiddenByUs) Run($"--dev-unhide \"{path}\"");
            _hiddenByUs.Clear();
            _applied = false;
            _lastError = "开启隐藏后控制设备失联，已尝试回退；若九尾狐仍不可见，请重启系统";
            return;
        }

        _applied = true;
        _lastError = null;
        Debug.WriteLine($"HidHide: cloaked {_hiddenByUs.Count} GameSir path(s), prevCloak={_prevCloak}");

        // Keep the hide list in step with the pad as it comes and goes: when the
        // user switches wired<->wireless, or unplugs/replugs, Windows enumerates
        // NEW instance paths. A one-shot hide at startup would leave the new
        // nodes visible (this is exactly why "wireless still shows as Xbox").
        _refreshTimer ??= new System.Threading.Timer(_ => Refresh(), null, 1500, 1500);
    }

    /// <summary>Reconcile the hide list with the currently-present GameSir nodes.
    /// Hides newly-appeared nodes and drops entries for nodes that vanished.
    /// Safe to call repeatedly and concurrently (serialised by <see cref="_lock"/>).</summary>
    public void Refresh()
    {
        if (!_applied) return;
        lock (_lock)
        {
            if (!_applied) return;
            try
            {
                var present = GameSirHidPaths();
                if (present.Length == 0) return;

                // Re-issue --dev-hide for EVERY present node every time. A replug
                // to the same USB port yields the SAME instance path, so a node
                // can look "already in the list" while the freshly enumerated
                // instance is actually NOT hidden — skipping it left the pad
                // visible for tens of seconds. --dev-hide is idempotent, so
                // re-hiding an already-hidden node is a harmless no-op. Batched
                // into one CLI process (spawning per node costs ~1 s).
                var presentSet = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
                var batch = new System.Text.StringBuilder();

                // Drop + unhide entries for nodes that have gone away, so the
                // list doesn't accumulate stale paths across connection changes.
                for (int i = _hiddenByUs.Count - 1; i >= 0; i--)
                    if (!presentSet.Contains(_hiddenByUs[i]))
                    {
                        batch.Append($"--dev-unhide \"{_hiddenByUs[i]}\" ");
                        _hiddenByUs.RemoveAt(i);
                    }

                foreach (var path in present)
                {
                    batch.Append($"--dev-hide \"{path}\" ");
                    if (!_hiddenByUs.Contains(path, StringComparer.OrdinalIgnoreCase))
                        _hiddenByUs.Add(path);
                }

                Run(batch.ToString().Trim());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HidHide.Refresh: {ex.Message}");
            }
        }
    }

    public void Disable()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        lock (_lock)
        {
            if (!_applied) return;

            foreach (var path in _hiddenByUs) Run($"--dev-unhide \"{path}\"");
            _hiddenByUs.Clear();

            // Restore the cloak state that existed before we touched it, so we don't
            // leave DSX's DS5 hiding on (or off) against its wishes.
            if (!_prevCloak) Run("--cloak-off");

            _applied = false;
        }
    }

    private bool ReadCloakState()
    {
        // --cloak-state prints "--cloak-on" or "--cloak-off".
        var outp = RunCapture("--cloak-state");
        return outp.Contains("--cloak-on");
    }

    private bool CanTalkToDriver()
    {
        var outp = RunCapture("--cloak-state");
        return !outp.Contains("Error");
    }

    private bool Run(string args)
    {
        var outp = RunCapture(args);
        return !outp.Contains("Error code");
    }

    private string RunCapture(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(CliPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return string.Empty;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            return stdout;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HidHideCLI {args}: {ex.Message}");
            return "Error";
        }
    }
}

using DS5Bridge.Core;

namespace DS5Bridge.App;

/// <summary>Owns and coordinates the whole bridge: device detection, the virtual
/// DualSense, the input pump, the haptic engine and the HidHide cloak.</summary>
public sealed class BridgeService : IDisposable
{
    private readonly Settings _settings;
    private DeviceWatcher? _watcher;
    private DeviceChangeWatcher? _devChange;
    private VirtualDS5? _virtual;
    private InputPump? _pump;
    private HapticEngine? _haptic;
    private HidHide? _hidHide;
    private GameSirPresence _presence = new(GameSirMode.None, null);

    public BridgeService(Settings settings) => _settings = settings;

    public bool Running { get; private set; }
    public GameSirPresence Presence => _presence;
    public VirtualDS5? Virtual => _virtual;

    /// <summary>Fired whenever status text should be refreshed by the UI.</summary>
    public event Action? StatusChanged;

    /// <summary>Diagnostic log line (rumble routing, hidhide actions).</summary>
    public event Action<string>? Log;

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "DS5Bridge.log");

    private void Emit(string line)
    {
        var s = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        Log?.Invoke(s);
        try { File.AppendAllText(LogPath, s + Environment.NewLine); } catch { }
    }

    public string StatusText()
    {
        if (!Running) return "已停止";
        var pad = _presence.Mode switch
        {
            GameSirMode.Wired => "九尾狐: 有线",
            GameSirMode.Wireless => "九尾狐: 无线",
            _ => "九尾狐: 未连接",
        };
        var vs = _virtual?.IsRunning == true ? "虚拟DS5: 运行中" : "虚拟DS5: 不可用";
        var haptic = _haptic is null ? "" :
            (_settings.HapticMode == 0 ? " | 震动: 原样" : " | 震动: 合成");
        var hide = _hidHide is not null ? " | 隐藏: 开" : "";
        return $"{pad} | {vs}{haptic}{hide}";
    }

    public void Start()
    {
        if (Running) return;

        _virtual = new VirtualDS5();

        _haptic = new HapticEngine(() => _pump?.CurrentSlot ?? -1, Emit)
        {
            Mode = _settings.HapticMode,
            PresetIndex = _settings.HapticPreset,
            Intensity = _settings.HapticIntensity,
        };
        _haptic.Start();
        // The game's rumble becomes the envelope the haptic engine modulates.
        _virtual.Rumble += r => _haptic.SetTarget(r.Left, r.Right);
        // Audio-haptics (voice-coil channels) drive the coil directly.
        _virtual.HapticAudio += r => _haptic.SetAudio(r.Left, r.Right);
        // Surface the game's raw output reports so we can see what it sends.
        _virtual.RawOutput += Emit;

        if (!_virtual.Start(out var err))
            Emit($"虚拟DS5 启动失败: {err}");
        else
            Emit($"虚拟DS5 已启动 [{_virtual.Status}] 震动模式:{(_settings.HapticMode == 0 ? "原样" : "合成")}");

        _watcher = new DeviceWatcher();
        _watcher.Changed += p =>
        {
            _presence = p;
            Emit($"设备变化: {p.Mode} ({p.InstanceId})");
            if (!p.Connected) _haptic?.Stop();
            // The pad re-enumerated (wired<->wireless, replug). Kick the hot
            // refresh window: wireless nodes can appear a second or two after
            // the mode flips, so a single Refresh() is not enough.
            if (_settings.HidePhysicalPad) OnDeviceChangeBurst();
            StatusChanged?.Invoke();
        };
        _presence = _watcher.Current;

        _pump = new InputPump(_virtual);
        _pump.Start();

        // Sub-millisecond replug reaction: WM_DEVICECHANGE -> reconcile hiding
        // immediately, so a freshly enumerated GameSir node never stays visible
        // long enough for a running game to latch onto it.
        if (_settings.HidePhysicalPad)
        {
            _devChange = new DeviceChangeWatcher();
            _devChange.Changed += OnDeviceChangeBurst;
            Emit(_devChange.IsActive ? "设备变更监听已启用" : $"设备变更监听未启用（仅轮询）: {_devChange.Diagnostic}");
        }

        ApplyHide(_settings.HidePhysicalPad);

        Running = true;
        StatusChanged?.Invoke();
    }

    /// <summary>Push the current haptic settings into the running engine (called
    /// live from the settings UI, so changes take effect without a restart).</summary>
    public void ApplyHapticSettings()
    {
        if (_haptic is null) return;
        _haptic.Mode = _settings.HapticMode;
        _haptic.PresetIndex = _settings.HapticPreset;
        _haptic.Intensity = _settings.HapticIntensity;
        StatusChanged?.Invoke();
    }

    /// <summary>Fire a short test pulse on the pads.</summary>
    public void TestRumble() => _haptic?.TestPulse();

    /// <summary>Turn the physical-pad cloak on/off live. Kept separate from Start
    /// so the settings UI can toggle it without restarting the bridge.</summary>
    public void ApplyHide(bool enabled)
    {
        if (enabled)
        {
            _hidHide ??= new HidHide();
            if (!_hidHide.Available)
            {
                Emit("HidHide 未找到，跳过隐藏");
                _hidHide = null;
                return;
            }
            if (!_hidHide.IsReachable())
            {
                Emit("HidHide 控制设备无响应（可能已锁定，需重启），已放弃隐藏");
                _hidHide = null;
                return;
            }
            _hidHide.Enable();
            if (_hidHide.LastError is { } err)
                Emit($"隐藏失败: {err}");
            else
                Emit("已隐藏物理手柄（HidHide）");
        }
        else if (_hidHide is not null)
        {
            _hidHide.Disable();
            if (_hidHide.LastError is { } err)
                Emit($"还原失败: {err}");
            else
                Emit("已还原物理手柄（HidHide off）");
            _hidHide = null;
        }
        StatusChanged?.Invoke();
    }

    /// <summary>Coalesced reaction to a device-arrival/removal burst: refresh the
    /// hide list twice (immediately + after enumeration settles) on a background
    /// thread, so the newly-enumerated GameSir nodes are hidden within ~100 ms.</summary>
    private int _devChangePending;
    private void OnDeviceChangeBurst()
    {
        if (Interlocked.Exchange(ref _devChangePending, 1) == 1) return;
        Task.Run(async () =>
        {
            try
            {
                Emit("设备变更 → 重新隐藏");
                // A wired<->wireless switch can enumerate the pad's nodes a
                // second or two after the mode flips (and the dongle never left,
                // so the arrival can be missed). Run a HOT window: refresh every
                // 120 ms for ~4 s so any exposed window is kept to well under a
                // frame or two of game polling.
                for (int i = 0; i < 34; i++)
                {
                    _hidHide?.Refresh();
                    await Task.Delay(120);
                }
            }
            catch (Exception ex) { Emit($"设备变更刷新出错: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _devChangePending, 0); }
        });
    }

    public void Stop()
    {
        if (!Running) return;
        _hidHide?.Disable();     // restore the physical pad's XInput signal
        _devChange?.Dispose();
        _pump?.Dispose();
        _haptic?.Dispose();
        _watcher?.Dispose();
        _virtual?.Dispose();
        _devChange = null;
        _hidHide = null;
        _pump = null;
        _haptic = null;
        _watcher = null;
        _virtual = null;
        Running = false;
        StatusChanged?.Invoke();
    }

    public void Dispose() => Stop();
}

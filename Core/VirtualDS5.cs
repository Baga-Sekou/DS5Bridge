using System.Diagnostics;

namespace DS5Bridge.Core;

/// <summary>Details of a rumble state the game asked the virtual DualSense to
/// play. Values are 0..255 per motor.</summary>
public readonly record struct RumbleState(byte Left, byte Right);

/// <summary>Owns the virtual DualSense controller (HIDMaestro) and surfaces the
/// output reports the game sends — rumble motors, LED, adaptive triggers.</summary>
public sealed class VirtualDS5 : IDisposable
{
    /// <summary>Raised on every output report the game sends to the virtual pad.
    /// Fired on the SDK's reader thread — keep handlers cheap.</summary>
    public event Action<RumbleState>? Rumble;

    /// <summary>Raw diagnostic line for every output report (throttled).</summary>
    public event Action<string>? RawOutput;

    /// <summary>Haptic audio level (0..255 per side) from the DualSense's
    /// voice-coil audio channels (3/4). Only fires on the composite profile.</summary>
    public event Action<RumbleState>? HapticAudio;

    public bool IsRunning { get; private set; }
    public bool AudioAvailable { get; private set; }

#if HIDMAESTRO
    private HIDMaestro.HMContext? _ctx;
    private HIDMaestro.HMController? _ctrl;
    private int _hapticChA = -1, _hapticChB = -1;
#endif

    public string Status { get; private set; } = "not started";

    public bool Start(out string error)
    {
        error = string.Empty;
#if HIDMAESTRO
        try
        {
            // Recover any OEM-name overrides a prior crash left behind so
            // joy.cpl isn't stuck showing a stale label.
            HIDMaestro.HMOemNameOverride.RecoverOrphans();

            _ctx = new HIDMaestro.HMContext();
            _ctx.LoadDefaultProfiles();

            // Prefer the composite profile: it carries the DualSense's USB audio
            // endpoints, whose channels 3/4 are the voice-coil haptics lanes that
            // games like Dave the Diver drive (audio-haptics, not motor rumble).
            // Fall back to the plain HID profile if the USB/IP transport is absent.
            string profileId = "dualsense";
            if (HIDMaestro.HMContext.IsUsbipBackendAvailable)
                profileId = "dualsense-composite";

            var profile = _ctx.GetProfile(profileId)
                ?? _ctx.GetProfile("dualsense")
                ?? throw new InvalidOperationException("HIDMaestro dualsense profile not found");

            // Plain (non-composite) profile needs the UMDF2 driver; composite
            // brings up its own USB/IP bus. Install the former unconditionally
            // and the latter only when we actually picked composite.
            if (!profile.RequiresUsbipBackend)
                _ctx.InstallDriver();

            _ctrl = _ctx.CreateController(profile);

            _ctrl.OutputDecoded += OnDecoded;
            _ctrl.OutputReceived += OnRaw;

            if (_ctrl.UsbAudio is { } audio)
            {
                AudioAvailable = true;
                var roles = audio.Output.ChannelRoles;
                for (int i = 0; i < roles.Count; i++)
                {
                    var r = roles[i].ToLowerInvariant();
                    if (r.Contains("haptic") && r.Contains("left")) _hapticChA = i;
                    else if (r.Contains("haptic") && r.Contains("right")) _hapticChB = i;
                }
                audio.Output.FramesReceived += OnAudioFrames;
                Status = $"running ({profile.Name}, {audio.Output.SampleRateHz}Hz/{audio.Output.Channels}ch audio)";
            }
            else
            {
                Status = $"running ({profile.Name})";
            }

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Status = "error: " + ex.Message;
            Dispose();
            return false;
        }
#else
        error = "HIDMaestro.Core.dll not present (virtual controller disabled)";
        Status = error;
        return false;
#endif
    }

#if HIDMAESTRO
    private void OnDecoded(object? sender, HIDMaestro.HMOutputDecodedEventArgs e)
    {
        try
        {
            byte l = 0, r = 0;
            if (e.Fields.TryGetValue("leftMotor", out var lv) && lv is byte lb) l = lb;
            if (e.Fields.TryGetValue("rightMotor", out var rv) && rv is byte rb) r = rb;
            RawOutput?.Invoke($"[decoded] id=0x{e.ReportId:X2} leftMotor={l} rightMotor={r} fields={string.Join(",", e.Fields.Keys)}");
            Rumble?.Invoke(new RumbleState(l, r));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OutputDecoded handler: {ex}");
        }
    }

    private byte[] _lastSig = Array.Empty<byte>();
    private int _rawCount;

    /// <summary>Called on the USB/IP audio pacing thread for each PCM window.
    /// Picks the two voice-coil haptic channels (3/4), computes their peak
    /// level, and reports a smoothed 0..255 envelope.</summary>
    private void OnAudioFrames(HIDMaestro.HMAudioOutput src, ReadOnlyMemory<byte> pcm)
    {
        try
        {
            int ch = src.Channels;
            if (ch <= 0 || (_hapticChA < 0 && _hapticChB < 0)) return;
            var span = pcm.Span;
            int frames = span.Length / (2 * ch);
            if (frames <= 0) return;

            float peakA = 0, peakB = 0;
            for (int f = 0; f < frames; f++)
            {
                int b0 = f * ch;
                if (_hapticChA >= 0)
                {
                    int o = (_hapticChA + b0) * 2;
                    short v = (short)(span[o] | (span[o + 1] << 8));
                    float a = v < 0 ? -v / 32768f : v / 32768f;
                    if (a > peakA) peakA = a;
                }
                if (_hapticChB >= 0)
                {
                    int o = (_hapticChB + b0) * 2;
                    short v = (short)(span[o] | (span[o + 1] << 8));
                    float a = v < 0 ? -v / 32768f : v / 32768f;
                    if (a > peakB) peakB = a;
                }
            }

            // Smooth so the coil doesn't jitter window-to-window.
            _audioL = _audioL * 0.6f + peakA * 0.4f;
            _audioR = _audioR * 0.6f + peakB * 0.4f;

            byte l = (byte)Math.Clamp(_audioL * 255f, 0, 255);
            byte r = (byte)Math.Clamp(_audioR * 255f, 0, 255);
            HapticAudio?.Invoke(new RumbleState(l, r));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HapticAudio handler: {ex}");
        }
    }
    private float _audioL, _audioR;

    private void OnRaw(HIDMaestro.HMController c, HIDMaestro.HMOutputPacket p)
    {
        try
        {
            _rawCount++;
            var span = p.Data.Span;
            int n = Math.Min(span.Length, 8);

            bool changed = n != _lastSig.Length;
            if (!changed)
                for (int i = 0; i < n; i++)
                    if (span[i] != _lastSig[i]) { changed = true; break; }

            if (changed || _rawCount <= 3)
            {
                _lastSig = span[..n].ToArray();
                int show = Math.Min(span.Length, 16);
                var hex = string.Join(" ", span[..show].ToArray().Select(b => b.ToString("X2")));
                RawOutput?.Invoke($"[out#{_rawCount}] src={p.Source} id=0x{p.ReportId:X2} len={span.Length}: {hex}");
            }

            // Secondary rumble source: XInput-wire packets (cmd+size+lo+hi).
            if (p.Source == HIDMaestro.HMOutputSource.XInput && span.Length >= 4)
                Rumble?.Invoke(new RumbleState(span[2], span[3]));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OutputReceived handler: {ex}");
        }
    }

    /// <summary>Push the physical pad's state to the virtual DualSense.</summary>
    public void Submit(in HIDMaestro.HMGamepadState state)
    {
        _ctrl?.SubmitState(in state);
    }

    public HIDMaestro.HMProfile? Profile => _ctrl?.Profile;
#endif

    public void Dispose()
    {
#if HIDMAESTRO
        _ctrl?.Dispose();
        _ctx?.Dispose();
        _ctrl = null;
        _ctx = null;
#endif
        IsRunning = false;
    }
}

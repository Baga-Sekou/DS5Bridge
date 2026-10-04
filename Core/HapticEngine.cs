using System.Diagnostics;

namespace DS5Bridge.Core;

/// <summary>Waveform-synthesis vibration engine.
///
/// The game hands us a low-frequency rumble ENVELOPE (DualSense leftMotor /
/// rightMotor, 0..255, updated at the game's own rate — often 60-120 Hz). The
/// naive path just forwards that to XInput, which the OS treats as a rotor-motor
/// level.
///
/// The GameSir 九尾狐 uses voice-coil (linear) motors, and we measured that its
/// firmware FOLLOWS amplitude changes all the way to XInput's ~125 Hz ceiling
/// (20/50 Hz pulsing is clearly distinguishable). So instead of forwarding a
/// flat level we synthesise a carrier waveform scaled by that envelope and push
/// it at the full 125 Hz XInput rate. That gives a tunable, consistent "motor
/// texture" the coil actually renders, rather than a DC level.
///
/// Mode 0 = raw passthrough (exactly what the game asked for).
/// Mode 1 = synthesis (envelope x carrier, per the selected preset).
/// </summary>
public sealed class HapticEngine : IDisposable
{
    public const int UpdateHz = 125;             // XInput's practical ceiling
    private const int PeriodMs = 1000 / UpdateHz;

    public enum Preset { Crisp = 0, Soft = 1, Strong = 2 }

    // frequency (Hz) and modulation depth (0..1) per preset
    private static readonly (double Freq, double Depth)[] Presets =
    {
        (14.0, 1.00),   // Crisp  — slow, full on/off pulses (unmistakable)
        (45.0, 0.55),   // Soft   — faster, gentler texture
        (22.0, 1.00),   // Strong — full-depth throb
    };

    private readonly Func<int> _slotProvider;
    private readonly Action<string>? _log;

    private volatile float _targetL;   // motor-rumble envelope 0..1
    private volatile float _targetR;
    private volatile float _audioL;    // haptic-audio envelope 0..1
    private volatile float _audioR;
    private volatile int _audioStamp;  // Environment.TickCount of last audio frame
    private int _mode;                 // 0 raw, 1 synth
    private int _preset = (int)Preset.Crisp;
    private float _intensity = 1.0f;   // output gain 0..1

    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public HapticEngine(Func<int> slotProvider, Action<string>? log = null)
    {
        _slotProvider = slotProvider;
        _log = log;
    }

    public int Mode { get => _mode; set => _mode = value; }
    public int PresetIndex { get => _preset; set => _preset = Math.Clamp(value, 0, Presets.Length - 1); }
    public float Intensity { get => _intensity; set => _intensity = Math.Clamp(value, 0f, 1f); }

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(Loop) { IsBackground = true, Name = "HapticEngine" };
        _thread.Start();
    }

    /// <summary>Set the game's motor-rumble envelope (0..255 each). Called on the
    /// HIDMaestro reader thread — must stay cheap.</summary>
    public void SetTarget(byte left, byte right)
    {
        _targetL = left / 255f;
        _targetR = right / 255f;

        // Log the moving<->stopped edges only, so a game session doesn't flood
        // the log but we can still see that rumble is arriving.
        bool moving = left > 8 || right > 8;
        if (moving != _wasMoving)
        {
            _wasMoving = moving;
            _log?.Invoke(moving ? $"游戏开始震动 (L={left} R={right})" : "游戏停止震动");
        }
    }
    private bool _wasMoving;

    /// <summary>Set the haptic-audio envelope (0..255 each) taken from the
    /// DualSense's voice-coil audio channels (3/4). Already a rich waveform, so
    /// it drives the coil directly rather than being modulated.</summary>
    public void SetAudio(byte left, byte right)
    {
        _audioL = left / 255f;
        _audioR = right / 255f;
        _audioStamp = Environment.TickCount;

        bool active = left > 8 || right > 8;
        if (active && !_audioWasActive)
            _log?.Invoke($"触觉音频活动 (L={left} R={right})");
        _audioWasActive = active;
    }
    private bool _audioWasActive;

    /// <summary>Play a short self-test pulse (bypasses the game). If a game is
    /// actively driving the target it will just override this immediately, so
    /// this is meant for the settings UI with no game running.</summary>
    public void TestPulse()
    {
        _ctsTest?.Cancel();
        _ctsTest = new CancellationTokenSource();
        var ct = _ctsTest.Token;
        Task.Run(async () =>
        {
            _targetL = _targetR = 0.85f;
            try { await Task.Delay(500, ct); } catch { }
            _targetL = _targetR = 0f;
        });
    }

    private CancellationTokenSource? _ctsTest;

    private void Loop()
    {
        double phase = 0;
        var sw = Stopwatch.StartNew();
        long next = 0;
        ushort lastL = 0, lastR = 0;
        bool sent = false;
        while (_cts is { IsCancellationRequested: false })
        {
            var (freq, depth) = Presets[_preset];
            float motorL, motorR;

            // Prefer the audio-haptics stream when it's alive (games like Dave
            // the Diver drive the voice coils over the DS5's audio channels, not
            // motor rumble). The audio is already the authored waveform, so it
            // is played as-is rather than modulated.
            bool audioActive = unchecked(Environment.TickCount - _audioStamp) < 120
                               && (_audioL > 0.01f || _audioR > 0.01f);

            if (audioActive)
            {
                motorL = _audioL;
                motorR = _audioR;
            }
            else if (_mode == 0)
            {
                // raw motor passthrough
                motorL = _targetL;
                motorR = _targetR;
            }
            else
            {
                // Square carrier (matches the on/off test that was clearly felt).
                double frac = phase / (2 * Math.PI);
                frac -= Math.Floor(frac);
                phase += 2 * Math.PI * freq / UpdateHz;
                if (phase > 2 * Math.PI * 4096) phase -= 2 * Math.PI * 4096;
                double sq = frac < 0.5 ? 1.0 : 0.0;

                float mod = (float)(1.0 - depth * (1.0 - sq));
                motorL = _targetL * mod;
                motorR = _targetR * mod;
            }

            motorL *= _intensity;
            motorR *= _intensity;

            ushort outL = (ushort)Math.Clamp(motorL * 65535f, 0, 65535);
            ushort outR = (ushort)Math.Clamp(motorR * 65535f, 0, 65535);

            // Idle shortcut: once motors are at rest, stop hammering XInput until
            // something changes (a game's rumble or a test pulse).
            bool idle = outL == 0 && outR == 0;
            if (idle && sent && lastL == 0 && lastR == 0)
            {
                next += PeriodMs;
                long sleepIdle = next - sw.ElapsedMilliseconds;
                if (sleepIdle > 0) Thread.Sleep((int)sleepIdle); else next = sw.ElapsedMilliseconds;
                continue;
            }

            int slot = _slotProvider();
            if (slot >= 0)
            {
                var vib = new XInput.XInputVibration { LeftMotorSpeed = outL, RightMotorSpeed = outR };
                XInput.SetState((uint)slot, ref vib);
                lastL = outL; lastR = outR; sent = true;
            }

            // steady 125 Hz pacing
            next += PeriodMs;
            long sleep = next - sw.ElapsedMilliseconds;
            if (sleep > 0) Thread.Sleep((int)sleep);
            else next = sw.ElapsedMilliseconds;
        }

        Stop();
    }

    public void Stop()
    {
        int slot = _slotProvider();
        if (slot >= 0)
        {
            var vib = new XInput.XInputVibration();
            XInput.SetState((uint)slot, ref vib);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(300);
        Stop();
    }
}

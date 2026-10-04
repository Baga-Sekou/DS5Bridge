using System.Diagnostics;

namespace DS5Bridge.Core;

/// <summary>Reads the physical GameSir pad over XInput at ~1 kHz and pushes the
/// state into the virtual DualSense. XInput-only, no allocation on the hot path.</summary>
public sealed class InputPump : IDisposable
{
    private readonly VirtualDS5 _virtual;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private int _slot = -1;

    public int CurrentSlot => Volatile.Read(ref _slot);
    public long Frames { get; private set; }

    public InputPump(VirtualDS5 virtualDs5) => _virtual = virtualDs5;

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "InputPump" };
        _thread.Start();
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_slot < 0 || !XInput.IsConnected((uint)_slot))
            {
                _slot = XInput.FindGameSirSlot();
                if (_slot < 0) { Thread.Sleep(250); continue; }
            }

            var state = new XInput.XInputState();
            if (XInput.GetState((uint)_slot, ref state) != 0)
            {
                Volatile.Write(ref _slot, -1);
                Thread.Sleep(100);
                continue;
            }

            Push(state.Gamepad);
            Frames++;

            // XInput polls at ~1 kHz; a 1 ms pace keeps the virtual report
            // rate aligned without busy-spinning a core.
            Thread.Sleep(1);
        }
    }

    private void Push(XInput.XInputGamepad g)
    {
#if HIDMAESTRO
        var buttons = MapButtons(g.Buttons);
        var hat = MapHat(g.Buttons);

        var profile = _virtual.Profile;
        HIDMaestro.HMGamepadState st = default;
        if (profile is not null)
        {
            st.Axes = HIDMaestro.HMGamepadStateHelpers.StandardAxes(
                profile,
                leftStickX:  Stick(g.LeftThumbX),
                leftStickY:  StickInv(g.LeftThumbY),
                rightStickX: Stick(g.RightThumbX),
                rightStickY: StickInv(g.RightThumbY),
                leftTrigger: g.LeftTrigger / 255f,
                rightTrigger: g.RightTrigger / 255f);
        }
        st.Buttons = buttons;
        st.Hat = hat;
        _virtual.Submit(in st);
#else
        _ = g;
#endif
    }

    // XInput thumb range is -32768..32767, Y inverted (up = +32767).
    // HIDMaestro expects [0..1] on signed axes with 0.5 = center.
    // XInput Y is positive-up; the DualSense Y axis is positive-down, so both
    // sticks' Y gets the inverse mapping (this is what DS4Windows does).
    private static float Stick(short v) => (v + 32768f) / 65535f;
    private static float StickInv(short v) => (32767f - v) / 65535f;

#if HIDMAESTRO
    private static HIDMaestro.HMButton MapButtons(ushort b)
    {
        var m = HIDMaestro.HMButton.None;
        if ((b & 0x1000) != 0) m |= HIDMaestro.HMButton.A;        // Cross
        if ((b & 0x2000) != 0) m |= HIDMaestro.HMButton.B;        // Circle
        if ((b & 0x4000) != 0) m |= HIDMaestro.HMButton.X;        // Square
        if ((b & 0x8000) != 0) m |= HIDMaestro.HMButton.Y;        // Triangle
        if ((b & 0x0100) != 0) m |= HIDMaestro.HMButton.LeftBumper;
        if ((b & 0x0200) != 0) m |= HIDMaestro.HMButton.RightBumper;
        // GameSir's "Select" maps to the DualSense touchpad click (what games
        // actually use to open menus), not the Share/Create button.
        if ((b & 0x0020) != 0) m |= HIDMaestro.HMButton.Touchpad;
        if ((b & 0x0010) != 0) m |= HIDMaestro.HMButton.Start;    // Options
        if ((b & 0x0040) != 0) m |= HIDMaestro.HMButton.LeftStick;
        if ((b & 0x0080) != 0) m |= HIDMaestro.HMButton.RightStick;
        return m;
    }

    private static HIDMaestro.HMHat MapHat(ushort b)
    {
        bool up = (b & 0x0001) != 0, down = (b & 0x0002) != 0;
        bool left = (b & 0x0004) != 0, right = (b & 0x0008) != 0;
        if (up && right) return HIDMaestro.HMHat.NorthEast;
        if (down && right) return HIDMaestro.HMHat.SouthEast;
        if (down && left) return HIDMaestro.HMHat.SouthWest;
        if (up && left) return HIDMaestro.HMHat.NorthWest;
        if (up) return HIDMaestro.HMHat.North;
        if (down) return HIDMaestro.HMHat.South;
        if (left) return HIDMaestro.HMHat.West;
        if (right) return HIDMaestro.HMHat.East;
        return HIDMaestro.HMHat.None;
    }
#endif

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(500);
    }
}

using System.Runtime.InteropServices;

namespace DS5Bridge.Core;

/// <summary>XInput wrapper. Finds the GameSir slot by capability VID/PID and
/// reads/submits state for it.</summary>
internal static class XInput
{
    public const ushort GameSirVendorId = 0x3537;

    private const int ErrorSuccess = 0;
    private const int ErrorDeviceNotConnected = 1167;

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputCapabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
    }

    private static readonly IntPtr[] Dlls =
    {
        NativeLibrary.Load("xinput1_4.dll"),
        NativeLibrary.Load("xinput1_3.dll"),
        NativeLibrary.Load("xinput9_1_0.dll"),
    };

    private static IntPtr _dll;

    private static IntPtr Dll
    {
        get
        {
            if (_dll == IntPtr.Zero)
            {
                foreach (var d in Dlls)
                {
                    if (NativeLibrary.TryGetExport(d, "XInputGetState", out _))
                    {
                        _dll = d;
                        break;
                    }
                }
            }
            return _dll;
        }
    }

    private static T Get<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Dll, name));

    public static int GetState(uint index, ref XInputState state)
    {
        if (Dll == IntPtr.Zero) return ErrorDeviceNotConnected;
        var fn = Get<GetStateDel>("XInputGetState");
        return fn(index, ref state);
    }

    public static int SetState(uint index, ref XInputVibration vib)
    {
        if (Dll == IntPtr.Zero) return ErrorDeviceNotConnected;
        var fn = Get<SetStateDel>("XInputSetState");
        return fn(index, ref vib);
    }

    private static int GetCaps(uint index, ref XInputCapabilities caps)
    {
        if (Dll == IntPtr.Zero) return ErrorDeviceNotConnected;
        var fn = Get<GetCapsDel>("XInputGetCapabilities");
        return fn(index, 0, ref caps);
    }

    /// <summary>Find the XInput slot whose capability VID/PID matches the
    /// GameSir vendor. Returns -1 if none. This handles the wired (0x1050)
    /// and dongle (0x103E) case alike since both are GameSir.</summary>
    public static int FindGameSirSlot()
    {
        for (uint i = 0; i < 4; i++)
        {
            var caps = new XInputCapabilities();
            if (GetCaps(i, ref caps) == ErrorSuccess)
                return (int)i;
        }
        return -1;
    }

    public static bool IsConnected(uint index)
    {
        var s = new XInputState();
        return GetState(index, ref s) == ErrorSuccess;
    }

    private delegate int GetStateDel(uint dwUserIndex, ref XInputState pState);
    private delegate int SetStateDel(uint dwUserIndex, ref XInputVibration pVibration);
    private delegate int GetCapsDel(uint dwUserIndex, uint dwFlags, ref XInputCapabilities pCapabilities);
}

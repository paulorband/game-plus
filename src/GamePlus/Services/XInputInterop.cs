using System.Runtime.InteropServices;

namespace GamePlus.Services;

[Flags]
public enum GamepadButton : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    // The triggers are analog in XInput; the poller reports them as buttons past a threshold (bits XInput leaves unused).
    LeftTrigger = 0x0400,
    RightTrigger = 0x0800,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public GamepadButton Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

internal static class XInputNative
{
    // xinput1_4.dll ships with Windows 8+ (present on all supported Windows 10/11 builds).
    private const string Dll = "xinput1_4.dll";

    [DllImport(Dll, EntryPoint = "XInputGetState")]
    public static extern int XInputGetState(int dwUserIndex, out XInputState pState);
}

public readonly record struct GamepadSnapshot(bool Connected, GamepadButton Buttons);

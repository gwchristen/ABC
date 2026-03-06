using System.Runtime.InteropServices;

namespace ABC.Helpers;

/// <summary>
/// Provides Win32 Raw Input API helpers for capturing HID keyboard input
/// at the OS level, bypassing WPF's focus system.
/// </summary>
internal sealed class RawInputInterop
{
    // Windows message constants
    public const int WM_INPUT = 0x00FF;
    private const int WM_KEYDOWN = 0x0100;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;

    // Virtual key / keyboard state helpers
    private const uint MAPVK_VK_TO_VSC = 0;

    public event Action<char>? CharReceived;
    public event Action? EnterPressed;

    // ---------------------------------------------------------------
    // Structs
    // ---------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr HwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public ulong ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER Header;
        public RAWKEYBOARD Keyboard;
    }

    // ---------------------------------------------------------------
    // P/Invoke declarations
    // ---------------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
        int uiNumDevices,
        int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    // ---------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------

    /// <summary>
    /// Registers the window for raw keyboard input using RIDEV_INPUTSINK so
    /// that WM_INPUT messages are delivered even when the window is not focused.
    /// Returns <c>true</c> on success.
    /// </summary>
    public bool Register(IntPtr hwnd)
    {
        var rid = new RAWINPUTDEVICE
        {
            UsagePage = 0x01, // Generic Desktop Controls
            Usage = 0x06,     // Keyboard
            Flags = RIDEV_INPUTSINK,
            HwndTarget = hwnd
        };

        return RegisterRawInputDevices(
            [rid],
            1,
            Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>
    /// Unregisters raw keyboard input for this process.
    /// </summary>
    public void Unregister()
    {
        var rid = new RAWINPUTDEVICE
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RIDEV_REMOVE,
            HwndTarget = IntPtr.Zero
        };

        RegisterRawInputDevices(
            [rid],
            1,
            Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>
    /// Parses a WM_INPUT lParam, converts the key to a Unicode character
    /// (if applicable), and raises <see cref="CharReceived"/> or
    /// <see cref="EnterPressed"/>.
    /// </summary>
    public void ProcessRawInput(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint dwSize = 0;

        // First call: retrieve required buffer size
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, headerSize);
        if (dwSize == 0)
            return;

        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            uint read = GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, headerSize);
            if (read != dwSize)
                return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);

            // Only handle keyboard, only on key-down
            if (raw.Header.Type != RIM_TYPEKEYBOARD)
                return;
            if (raw.Keyboard.Message != WM_KEYDOWN)
                return;

            uint vKey = raw.Keyboard.VKey;
            uint scanCode = raw.Keyboard.MakeCode;
            if (scanCode == 0)
                scanCode = MapVirtualKey(vKey, MAPVK_VK_TO_VSC);

            // Enter key
            if (vKey == 0x0D) // VK_RETURN
            {
                EnterPressed?.Invoke();
                return;
            }

            // Convert virtual key to Unicode character
            var keyState = new byte[256];
            GetKeyboardState(keyState);

            var sb = new System.Text.StringBuilder(4);
            int result = ToUnicode(vKey, scanCode, keyState, sb, sb.Capacity, 0);
            if (result == 1 && sb.Length > 0)
            {
                char c = sb[0];
                if (!char.IsControl(c))
                    CharReceived?.Invoke(c);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

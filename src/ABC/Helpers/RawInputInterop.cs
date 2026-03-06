using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace ABC.Helpers;

internal sealed class RawInputInterop : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint VK_RETURN = 0x0D;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER Header;
        public RAWKEYBOARD Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private readonly HwndSource _hwndSource;
    private bool _disposed;

    public event EventHandler<char>? CharacterReceived;
    public event EventHandler? EnterKeyReceived;

    public RawInputInterop(IntPtr hwnd)
    {
        _hwndSource = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("Could not get HwndSource for window.");
        Register(hwnd);
        _hwndSource.AddHook(WndProc);
    }

    private static void Register(IntPtr hwnd)
    {
        var device = new RAWINPUTDEVICE
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RIDEV_INPUTSINK,
            Target = hwnd
        };
        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
    }

    private static void Unregister()
    {
        var device = new RAWINPUTDEVICE
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RIDEV_REMOVE,
            Target = IntPtr.Zero
        };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
            ProcessRawInput(lParam);
        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
                return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.Header.Type != RIM_TYPEKEYBOARD) return;
            if (raw.Keyboard.Message != WM_KEYDOWN && raw.Keyboard.Message != WM_SYSKEYDOWN) return;

            uint vKey = raw.Keyboard.VKey;

            if (vKey == VK_RETURN)
            {
                EnterKeyReceived?.Invoke(this, EventArgs.Empty);
                return;
            }

            uint scanCode = MapVirtualKey(vKey, MAPVK_VK_TO_VSC);
            var keyState = new byte[256];
            GetKeyboardState(keyState);
            var sb = new StringBuilder(2);
            int result = ToUnicode(vKey, scanCode, keyState, sb, sb.Capacity, 0);
            if (result == 1)
                CharacterReceived?.Invoke(this, sb[0]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hwndSource.RemoveHook(WndProc);
        Unregister();
    }
}

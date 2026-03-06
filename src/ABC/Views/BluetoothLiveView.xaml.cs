using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ABC.Helpers;
using ABC.ViewModels;

namespace ABC.Views;

public partial class BluetoothLiveView : UserControl
{
    private HwndSource? _hwndSource;
    private readonly RawInputInterop _rawInput = new();

    public BluetoothLiveView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == null)
            return;

        window.Activated += OnWindowActivated;
        window.Deactivated += OnWindowDeactivated;

        if (DataContext is BluetoothLiveViewModel vm)
            vm.IsWindowFocused = window.IsActive;

        // Set up Raw Input via WndProc hook so HID keyboard events are
        // captured regardless of which WPF control currently has focus.
        _hwndSource = PresentationSource.FromVisual(window) as HwndSource;
        if (_hwndSource != null && _rawInput.Register(_hwndSource.Handle))
        {
            _hwndSource.AddHook(WndProc);
            _rawInput.CharReceived += OnRawCharReceived;
            _rawInput.EnterPressed += OnRawEnterPressed;

            // Suppress barcode keystrokes from reaching focused TextBoxes
            // while HID listening is active.
            window.PreviewKeyDown += OnWindowPreviewKeyDown;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _rawInput.CharReceived -= OnRawCharReceived;
        _rawInput.EnterPressed -= OnRawEnterPressed;

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        _rawInput.Unregister();

        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
            window.PreviewKeyDown -= OnWindowPreviewKeyDown;
        }
    }

    // ---------------------------------------------------------------
    // WndProc hook — forwards WM_INPUT messages to RawInputInterop
    // ---------------------------------------------------------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == RawInputInterop.WM_INPUT)
        {
            if (DataContext is BluetoothLiveViewModel { IsHidListening: true })
                _rawInput.ProcessRawInput(lParam);
        }

        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------
    // Raw Input event handlers
    // ---------------------------------------------------------------

    private void OnRawCharReceived(char c)
    {
        if (DataContext is BluetoothLiveViewModel vm)
            vm.ProcessHidText(c.ToString());
    }

    private void OnRawEnterPressed()
    {
        if (DataContext is BluetoothLiveViewModel vm)
            vm.ProcessHidKey(Key.Enter);
    }

    // ---------------------------------------------------------------
    // Window activation — kept so ShowFocusWarning binding still works
    // ---------------------------------------------------------------

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is BluetoothLiveViewModel vm)
            vm.IsWindowFocused = true;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is BluetoothLiveViewModel vm)
            vm.IsWindowFocused = false;
    }

    // ---------------------------------------------------------------
    // Suppress barcode keystrokes from reaching focused TextBoxes
    // ---------------------------------------------------------------

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not BluetoothLiveViewModel { IsHidListening: true })
            return;

        // Raw Input already captured this keystroke; mark it as handled so
        // it does not also type into whichever TextBox currently has focus.
        // Key.System is excluded so that system shortcuts (e.g., Alt+F4 to
        // close the window) continue to work normally during HID scanning.
        if (e.Key != Key.System)
            e.Handled = true;
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ABC.Helpers;
using ABC.ViewModels;

namespace ABC.Views;

public partial class BluetoothLiveView : UserControl
{
    private RawInputInterop? _rawInput;

    public BluetoothLiveView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == null)
            return;

        window.Activated += OnWindowActivated;
        window.Deactivated += OnWindowDeactivated;

        if (DataContext is BluetoothLiveViewModel vm)
        {
            vm.IsWindowFocused = window.IsActive;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
        }

        if (DataContext is BluetoothLiveViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;

        StopRawInput();
    }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BluetoothLiveViewModel.IsHidListening)) return;
        if (DataContext is BluetoothLiveViewModel vm)
        {
            if (vm.IsHidListening)
                StartRawInput();
            else
                StopRawInput();
        }
    }

    private void StartRawInput()
    {
        if (_rawInput != null) return;
        var window = Window.GetWindow(this);
        if (window == null) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            _rawInput = new RawInputInterop(hwnd);
            _rawInput.CharacterReceived += OnRawCharacterReceived;
            _rawInput.EnterKeyReceived += OnRawEnterKeyReceived;
        }
        catch (InvalidOperationException)
        {
            _rawInput = null;
        }
    }

    private void StopRawInput()
    {
        if (_rawInput == null) return;
        _rawInput.CharacterReceived -= OnRawCharacterReceived;
        _rawInput.EnterKeyReceived -= OnRawEnterKeyReceived;
        _rawInput.Dispose();
        _rawInput = null;
    }

    private void OnRawCharacterReceived(object? sender, char c)
    {
        if (DataContext is BluetoothLiveViewModel vm)
            vm.ProcessHidText(c.ToString());
    }

    private void OnRawEnterKeyReceived(object? sender, EventArgs e)
    {
        if (DataContext is BluetoothLiveViewModel vm)
            vm.ProcessHidKey(Key.Return);
    }
}

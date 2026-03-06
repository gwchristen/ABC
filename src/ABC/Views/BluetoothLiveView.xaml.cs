using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ABC.Helpers;
using ABC.ViewModels;

namespace ABC.Views;

public partial class BluetoothLiveView : UserControl
{
    private RawInputInterop? _rawInput;

    public BluetoothLiveView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
            window.PreviewTextInput += OnWindowPreviewTextInput;
            window.PreviewKeyDown += OnWindowPreviewKeyDown;

            if (DataContext is BluetoothLiveViewModel vm)
            {
                vm.IsWindowFocused = window.IsActive;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
            window.PreviewTextInput -= OnWindowPreviewTextInput;
            window.PreviewKeyDown -= OnWindowPreviewKeyDown;
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

    private void OnWindowPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (DataContext is BluetoothLiveViewModel vm && vm.IsHidListening)
            e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is BluetoothLiveViewModel vm && vm.IsHidListening)
            e.Handled = true;
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
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
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

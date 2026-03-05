using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ABC.ViewModels;

namespace ABC.Views;

public partial class BluetoothLiveView : UserControl
{
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
                vm.IsWindowFocused = window.IsActive;
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
        if (!ShouldProcessHidInput()) return;
        if (DataContext is BluetoothLiveViewModel vm)
            vm.ProcessHidText(e.Text);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return && e.Key != Key.Enter) return;
        if (!ShouldProcessHidInput()) return;
        if (DataContext is BluetoothLiveViewModel vm)
            vm.ProcessHidKey(e.Key);
    }

    private static bool ShouldProcessHidInput() => Keyboard.FocusedElement is not TextBox;
}

using System;
using System.Windows;
using System.Windows.Interop;
using ABC.ViewModels;

namespace ABC;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += OnWindowClosing;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);
    }

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVICEARRIVAL = 0x8007;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
        {
            int eventType = wParam.ToInt32();
            if (eventType == DBT_DEVICEREMOVECOMPLETE)
            {
                if (DataContext is MainViewModel vm)
                    vm.OnUsbDeviceRemoved();
            }
            else if (eventType == DBT_DEVICEARRIVAL)
            {
                if (DataContext is MainViewModel vm)
                    vm.OnUsbDeviceArrived();
            }
        }
        return IntPtr.Zero;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OnShutdown();
    }
}

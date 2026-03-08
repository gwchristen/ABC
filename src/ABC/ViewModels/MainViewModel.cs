using System.Windows.Input;
using ABC.Services;

namespace ABC.ViewModels;

public class MainViewModel : ViewModelBase
{
    private string _statusMessage = "Ready";
    private int _totalBarcodeCount;

    public UsbDownloadViewModel UsbDownload { get; }
    public BluetoothLiveViewModel BluetoothLive { get; }
    public RangeMakerViewModel RangeMaker { get; }
    public ScannerSettingsViewModel ScannerSettings { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int TotalBarcodeCount
    {
        get => _totalBarcodeCount;
        private set => SetProperty(ref _totalBarcodeCount, value);
    }

    public bool IsDarkTheme => ThemeService.Instance.IsDarkTheme;

    public ICommand ToggleThemeCommand { get; }

    public MainViewModel()
    {
        UsbDownload = new UsbDownloadViewModel();
        BluetoothLive = new BluetoothLiveViewModel();
        RangeMaker = new RangeMakerViewModel();
        ScannerSettings = new ScannerSettingsViewModel();

        UsbDownload.StatusChanged += OnChildStatusChanged;
        BluetoothLive.StatusChanged += OnChildStatusChanged;
        RangeMaker.StatusChanged += OnChildStatusChanged;
        ScannerSettings.StatusChanged += OnChildStatusChanged;

        UsbDownload.BarcodeCountChanged += OnBarcodeCountChanged;
        BluetoothLive.BarcodeCountChanged += OnBarcodeCountChanged;
        RangeMaker.BarcodeCountChanged += OnBarcodeCountChanged;

        ToggleThemeCommand = new RelayCommand(_ =>
        {
            ThemeService.Instance.ToggleTheme();
            OnPropertyChanged(nameof(IsDarkTheme));
        });
    }

    private void OnChildStatusChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    private void OnBarcodeCountChanged(object? sender, EventArgs e)
    {
        TotalBarcodeCount = UsbDownload.BarcodeCount + BluetoothLive.BarcodeCount + RangeMaker.BarcodeCount;
    }
}

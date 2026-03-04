namespace ABC.ViewModels;

public class MainViewModel : ViewModelBase
{
    private string _statusMessage = "Ready";
    private int _totalBarcodeCount;

    public UsbDownloadViewModel UsbDownload { get; }
    public BluetoothLiveViewModel BluetoothLive { get; }

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

    public MainViewModel()
    {
        UsbDownload = new UsbDownloadViewModel();
        BluetoothLive = new BluetoothLiveViewModel();

        UsbDownload.StatusChanged += OnChildStatusChanged;
        BluetoothLive.StatusChanged += OnChildStatusChanged;

        UsbDownload.BarcodeCountChanged += OnBarcodeCountChanged;
        BluetoothLive.BarcodeCountChanged += OnBarcodeCountChanged;
    }

    private void OnChildStatusChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    private void OnBarcodeCountChanged(object? sender, EventArgs e)
    {
        TotalBarcodeCount = UsbDownload.BarcodeCount + BluetoothLive.BarcodeCount;
    }
}

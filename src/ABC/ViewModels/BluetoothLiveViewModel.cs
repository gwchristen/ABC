using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ABC.Models;
using ABC.Services;

namespace ABC.ViewModels;

public class BluetoothLiveViewModel : ViewModelBase
{
    private readonly IBluetoothScanService _bluetoothService;
    private readonly IFileExportService _fileExportService;

    private ObservableCollection<string> _availablePorts = new();
    private string? _selectedPort;
    private ObservableCollection<BarcodeEntry> _barcodes = new();
    private string _liveText = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _fileName = GenerateDefaultFileName();
    private bool _saveAsCsv;
    private bool _isConnected;
    private bool _showTimestamps = true;
    private bool _isBleMode;
    private bool _isScanningBle;
    private ObservableCollection<BleDeviceInfo> _bleDevices = new();
    private BleDeviceInfo? _selectedBleDevice;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? BarcodeCountChanged;

    public ObservableCollection<string> AvailablePorts
    {
        get => _availablePorts;
        private set => SetProperty(ref _availablePorts, value);
    }

    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            SetProperty(ref _selectedPort, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int BarcodeCount => _barcodes.Count;

    public string LiveText
    {
        get => _liveText;
        private set => SetProperty(ref _liveText, value);
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetProperty(ref _saveDirectory, value);
    }

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public bool SaveAsCsv
    {
        get => _saveAsCsv;
        set => SetProperty(ref _saveAsCsv, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            SetProperty(ref _isConnected, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowTimestamps
    {
        get => _showTimestamps;
        set => SetProperty(ref _showTimestamps, value);
    }

    public bool IsBleMode
    {
        get => _isBleMode;
        set
        {
            SetProperty(ref _isBleMode, value);
            OnPropertyChanged(nameof(IsSppMode));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsSppMode => !_isBleMode;

    public bool IsScanningBle
    {
        get => _isScanningBle;
        private set
        {
            SetProperty(ref _isScanningBle, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ObservableCollection<BleDeviceInfo> BleDevices => _bleDevices;

    public BleDeviceInfo? SelectedBleDevice
    {
        get => _selectedBleDevice;
        set
        {
            SetProperty(ref _selectedBleDevice, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand RefreshPortsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearDisplayCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ScanBleCommand { get; }
    public ICommand ConnectBleCommand { get; }

    public BluetoothLiveViewModel() : this(new BluetoothScanService(), new FileExportService()) { }

    public BluetoothLiveViewModel(IBluetoothScanService bluetoothService, IFileExportService fileExportService)
    {
        _bluetoothService = bluetoothService;
        _fileExportService = fileExportService;
        _bluetoothService.BarcodeReceived += OnBarcodeReceived;
        _bluetoothService.BleDeviceDiscovered += OnBleDeviceDiscovered;
        _bluetoothService.Disconnected += OnServiceDisconnected;

        _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        ConnectCommand = new RelayCommand(_ => Connect(), _ => !IsConnected && SelectedPort != null);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        ClearDisplayCommand = new RelayCommand(_ => ClearDisplay());
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => _barcodes.Count > 0);
        ScanBleCommand = new RelayCommand(async _ => await ToggleBleDiscoveryAsync(), _ => !IsConnected);
        ConnectBleCommand = new RelayCommand(async _ => await ConnectBleAsync(), _ => !IsConnected && SelectedBleDevice != null);

        RefreshPorts();
    }

    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var port in _bluetoothService.GetAvailableComPorts().OrderBy(p => p))
            AvailablePorts.Add(port);

        if (AvailablePorts.Count > 0 && SelectedPort == null)
            SelectedPort = AvailablePorts[0];
    }

    private void Connect()
    {
        if (SelectedPort is null)
        {
            StatusChanged?.Invoke(this, "No COM port selected.");
            return;
        }

        bool connected = _bluetoothService.Connect(SelectedPort);
        if (connected)
        {
            IsConnected = true;
            StatusChanged?.Invoke(this, $"Connected to {SelectedPort} at 115200 baud. Waiting for scans...");
        }
        else
        {
            StatusChanged?.Invoke(this, $"Failed to connect to {SelectedPort}.");
        }
    }

    private async Task ConnectBleAsync()
    {
        if (SelectedBleDevice is null)
        {
            StatusChanged?.Invoke(this, "No BLE device selected.");
            return;
        }

        StatusChanged?.Invoke(this, $"Connecting to {SelectedBleDevice.DisplayName}...");
        bool connected = await _bluetoothService.ConnectBleAsync(SelectedBleDevice.BluetoothAddress);
        if (connected)
        {
            IsConnected = true;
            _bluetoothService.StopBleDiscovery();
            IsScanningBle = false;
            StatusChanged?.Invoke(this, $"Connected to {SelectedBleDevice.DisplayName}. Waiting for scans...");
        }
        else
        {
            StatusChanged?.Invoke(this, $"Failed to connect to {SelectedBleDevice.DisplayName}.");
        }
    }

    private async Task ToggleBleDiscoveryAsync()
    {
        if (IsScanningBle)
        {
            _bluetoothService.StopBleDiscovery();
            IsScanningBle = false;
            StatusChanged?.Invoke(this, "BLE scan stopped.");
        }
        else
        {
            _bleDevices.Clear();
            SelectedBleDevice = null;
            await _bluetoothService.StartBleDiscoveryAsync();
            IsScanningBle = true;
            StatusChanged?.Invoke(this, "Scanning for BLE devices...");
        }
    }

    private void Disconnect()
    {
        _bluetoothService.Disconnect();
        IsConnected = false;
        IsScanningBle = false;
        StatusChanged?.Invoke(this, "Disconnected from Bluetooth scanner.");
    }

    private void ClearDisplay()
    {
        _barcodes.Clear();
        LiveText = string.Empty;
        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBarcodeReceived(object? sender, BarcodeEntry entry)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            entry.SequenceNumber = _barcodes.Count + 1;
            _barcodes.Add(entry);

            string line = ShowTimestamps
                ? $"[{entry.Timestamp:HH:mm:ss}] {entry.Barcode}"
                : entry.Barcode;

            LiveText += line + Environment.NewLine;
            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, $"Received barcode: {entry.Barcode} ({_barcodes.Count} total)");
        });
    }

    private void OnBleDeviceDiscovered(object? sender, BleDeviceInfo deviceInfo)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // De-duplicate by Bluetooth address
            if (_bleDevices.All(d => d.BluetoothAddress != deviceInfo.BluetoothAddress))
            {
                _bleDevices.Add(deviceInfo);
                StatusChanged?.Invoke(this, $"Found BLE device: {deviceInfo.DisplayName}");
            }
        });
    }

    private void OnServiceDisconnected(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsConnected = false;
            StatusChanged?.Invoke(this, "BLE device disconnected.");
        });
    }

    private void BrowseDirectory()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Save Directory",
                InitialDirectory = SaveDirectory
            };

            if (dialog.ShowDialog() == true)
                SaveDirectory = dialog.FolderName;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Could not open folder browser: {ex.Message}");
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            StatusChanged?.Invoke(this, "Please select a save directory.");
            return;
        }

        if (string.IsNullOrWhiteSpace(FileName))
            FileName = GenerateDefaultFileName();

        string extension = SaveAsCsv ? ".csv" : ".txt";
        string fullPath = Path.Combine(SaveDirectory, FileName + extension);

        var snapshot = _barcodes.ToList();
        StatusChanged?.Invoke(this, $"Saving to {fullPath}...");
        try
        {
            bool saved = await Task.Run(() =>
                SaveAsCsv
                    ? _fileExportService.SaveAsCsv(fullPath, snapshot)
                    : _fileExportService.SaveAsText(fullPath, snapshot));

            StatusChanged?.Invoke(this, saved
                ? $"Saved {snapshot.Count} barcode(s) to {fullPath}."
                : "Failed to save file.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
        }
    }

    private static string GenerateDefaultFileName()
        => $"scan_{DateTime.Now:yyyyMMdd_HHmmss}";
}


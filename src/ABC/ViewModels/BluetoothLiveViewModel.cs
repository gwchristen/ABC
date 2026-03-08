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
    private readonly Dictionary<string, int> _barcodeCounts = new();
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
    private bool _isWindowFocused = true;
    private bool _isHidMode;
    private readonly StringBuilder _hidBuffer = new();
    private readonly DispatcherTimer _hidTimer;
    private const int ValidBarcodeLength = 17;
    private const int HidBufferFlushTimeoutMs = 150;

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

    public int DuplicateCount => _barcodes.Count(b => b.IsDuplicate);

    public bool HasDuplicates => DuplicateCount > 0;

    public int InvalidLengthCount => _barcodes.Count(b => b.IsInvalidLength);

    public bool HasInvalidLength => InvalidLengthCount > 0;

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

    private bool _appendToFile;
    public bool AppendToFile
    {
        get => _appendToFile;
        set => SetProperty(ref _appendToFile, value);
    }

    private string _appendFilePath = string.Empty;
    public string AppendFilePath
    {
        get => _appendFilePath;
        set
        {
            SetProperty(ref _appendFilePath, value);
            OnPropertyChanged(nameof(AppendFileName));
        }
    }

    public string AppendFileName => string.IsNullOrEmpty(_appendFilePath)
        ? string.Empty
        : System.IO.Path.GetFileName(_appendFilePath);

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
            if (SetProperty(ref _isBleMode, value))
            {
                OnPropertyChanged(nameof(IsSppMode));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSppMode => !_isBleMode && !_isHidMode;

    public bool IsHidMode
    {
        get => _isHidMode;
        set
        {
            if (SetProperty(ref _isHidMode, value))
            {
                if (value)
                    _isBleMode = false;
                OnPropertyChanged(nameof(IsBleMode));
                OnPropertyChanged(nameof(IsSppMode));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsHidListening => _bluetoothService.IsHidListening;

    // Raw Input with RIDEV_INPUTSINK captures HID keystrokes even when the
    // window is in the background, so no focus warning is needed.
    // The property is retained because the XAML visibility binding uses it.
    public bool ShowFocusWarning => false;

    public bool IsWindowFocused
    {
        set
        {
            if (_isWindowFocused != value)
            {
                _isWindowFocused = value;
                OnPropertyChanged(nameof(ShowFocusWarning));
            }
        }
    }

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
    public ICommand BrowseAppendFileCommand { get; }
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
        BrowseAppendFileCommand = new RelayCommand(_ => BrowseAppendFile());
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
        _barcodeCounts.Clear();
        LiveText = string.Empty;
        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(InvalidLengthCount));
        OnPropertyChanged(nameof(HasInvalidLength));
    }

    private void OnBarcodeReceived(object? sender, BarcodeEntry entry)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            entry.SequenceNumber = _barcodes.Count + 1;

            _barcodeCounts.TryGetValue(entry.Barcode, out int count);
            _barcodeCounts[entry.Barcode] = count + 1;

            bool isDuplicate = count > 0;
            if (isDuplicate)
            {
                entry.IsDuplicate = true;
                if (count == 1)
                {
                    // Mark the first occurrence as a duplicate too
                    var first = _barcodes.FirstOrDefault(b => b.Barcode == entry.Barcode);
                    if (first != null)
                        first.IsDuplicate = true;
                }
            }

            _barcodes.Add(entry);

            entry.IsInvalidLength = entry.Barcode.Length != ValidBarcodeLength;

            string prefix = isDuplicate ? "[DUP] " : string.Empty;
            string line = ShowTimestamps
                ? $"[{entry.Timestamp:HH:mm:ss}] {prefix}{entry.Barcode}"
                : $"{prefix}{entry.Barcode}";

            LiveText += line + Environment.NewLine;
            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(DuplicateCount));
            OnPropertyChanged(nameof(HasDuplicates));
            OnPropertyChanged(nameof(InvalidLengthCount));
            OnPropertyChanged(nameof(HasInvalidLength));
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

    private void BrowseAppendFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select File to Append To",
                Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                InitialDirectory = string.IsNullOrWhiteSpace(AppendFilePath)
                    ? SaveDirectory
                    : Path.GetDirectoryName(AppendFilePath) ?? SaveDirectory
            };

            if (dialog.ShowDialog() == true)
                AppendFilePath = dialog.FileName;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Could not open file browser: {ex.Message}");
        }
    }

    private async Task SaveAsync()
    {
        if (AppendToFile)
        {
            if (string.IsNullOrWhiteSpace(AppendFilePath))
            {
                StatusChanged?.Invoke(this, "Please select a file to append to.");
                return;
            }

            bool isCsv = string.Equals(Path.GetExtension(AppendFilePath), ".csv", StringComparison.OrdinalIgnoreCase);

            var snapshot = _barcodes.ToList();
            StatusChanged?.Invoke(this, $"Appending to {AppendFilePath}...");
            try
            {
                bool saved = await Task.Run(() => isCsv
                    ? _fileExportService.AppendAsCsv(AppendFilePath, snapshot)
                    : _fileExportService.AppendAsText(AppendFilePath, snapshot));

                StatusChanged?.Invoke(this, saved
                    ? $"Appended {snapshot.Count} barcode(s) to {AppendFilePath}."
                    : "Failed to save file.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            StatusChanged?.Invoke(this, "Please select a save directory.");
            return;
        }

        if (string.IsNullOrWhiteSpace(FileName))
            FileName = GenerateDefaultFileName();

        string extension = SaveAsCsv ? ".csv" : ".txt";
        string fullPath = Path.Combine(SaveDirectory, FileName + extension);

        var barcodeSnapshot = _barcodes.ToList();
        StatusChanged?.Invoke(this, $"Saving to {fullPath}...");
        try
        {
            bool saved = await Task.Run(() => SaveAsCsv
                ? _fileExportService.SaveAsCsv(fullPath, barcodeSnapshot)
                : _fileExportService.SaveAsText(fullPath, barcodeSnapshot));

            StatusChanged?.Invoke(this, saved
                ? $"Saved {barcodeSnapshot.Count} barcode(s) to {fullPath}."
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


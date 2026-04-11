using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ABC.Models;
using ABC.Services;

namespace ABC.ViewModels;

public class ScannerSettingsViewModel : ViewModelBase
{
    private readonly IScannerService _scannerService;

    private ScannerInfo? _scannerInfo;
    private ObservableCollection<int> _availablePorts = new();
    private int _selectedPort;
    private bool _isBusy;
    private bool _isConnected;

    // Param 2: Buzzer (Toggle)
    private bool _buzzerEnabled;
    // Param 4: Reject Redundant Barcode (Dropdown 0-2)
    private int _rejectRedundantIndex;
    // Param 7: Low-Battery Indication (Dropdown 0-3)
    private int _lowBatteryIndex;
    // Param 10: Host Connect Beep (Toggle)
    private bool _hostConnectBeepEnabled;
    // Param 11: Host Complete Beep (Toggle)
    private bool _hostCompleteBeepEnabled;
    // Param 17: Scanner On-Time (Numeric 1-255)
    private string _scannerOnTime = "10";
    // Param 34: Max Barcode Length (Numeric 1-30)
    private string _maxBarcodeLength = "30";
    // Param 35: Store RTC Timestamp (Toggle)
    private bool _storeTimestampEnabled;

    public event EventHandler<string>? StatusChanged;

    public ScannerInfo? ScannerInfo
    {
        get => _scannerInfo;
        private set => SetProperty(ref _scannerInfo, value);
    }

    public ObservableCollection<int> AvailablePorts
    {
        get => _availablePorts;
        private set => SetProperty(ref _availablePorts, value);
    }

    public int SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetProperty(ref _isBusy, value);
            CommandManager.InvalidateRequerySuggested();
        }
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

    // Parameter properties
    public bool BuzzerEnabled
    {
        get => _buzzerEnabled;
        set => SetProperty(ref _buzzerEnabled, value);
    }

    public int RejectRedundantIndex
    {
        get => _rejectRedundantIndex;
        set => SetProperty(ref _rejectRedundantIndex, value);
    }

    public int LowBatteryIndex
    {
        get => _lowBatteryIndex;
        set => SetProperty(ref _lowBatteryIndex, value);
    }

    public bool HostConnectBeepEnabled
    {
        get => _hostConnectBeepEnabled;
        set => SetProperty(ref _hostConnectBeepEnabled, value);
    }

    public bool HostCompleteBeepEnabled
    {
        get => _hostCompleteBeepEnabled;
        set => SetProperty(ref _hostCompleteBeepEnabled, value);
    }

    public string ScannerOnTime
    {
        get => _scannerOnTime;
        set
        {
            if (int.TryParse(value, out int parsed) && parsed >= 1 && parsed <= 255)
                SetProperty(ref _scannerOnTime, value);
            else
                StatusChanged?.Invoke(this, "Scanner On-Time must be a number between 1 and 255.");
        }
    }

    public string MaxBarcodeLength
    {
        get => _maxBarcodeLength;
        set
        {
            if (int.TryParse(value, out int parsed) && parsed >= 1 && parsed <= 30)
                SetProperty(ref _maxBarcodeLength, value);
            else
                StatusChanged?.Invoke(this, "Max Barcode Length must be a number between 1 and 30.");
        }
    }

    public bool StoreTimestampEnabled
    {
        get => _storeTimestampEnabled;
        set => SetProperty(ref _storeTimestampEnabled, value);
    }

    // Dropdown option lists
    public List<string> RejectRedundantOptions { get; } = new() { "Off", "If consecutive", "Always" };
    public List<string> LowBatteryOptions { get; } = new() { "Off", "LED only", "Buzzer only", "LED + Buzzer" };

    // Commands
    public ICommand DetectScannersCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ReadAllCommand { get; }
    public ICommand ApplyAllCommand { get; }
    public ICommand ResetDefaultsCommand { get; }

    public ScannerSettingsViewModel() : this(CreateDefaultScannerService()) { }

    public ScannerSettingsViewModel(IScannerService scannerService)
    {
        _scannerService = scannerService;

        DetectScannersCommand = new AsyncRelayCommand(async () => await DetectScannersAsync(), () => !IsBusy);
        ConnectCommand = new AsyncRelayCommand(async () => await ConnectAsync(), () => !IsBusy && SelectedPort > 0);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        ReadAllCommand = new AsyncRelayCommand(async () => await ReadAllAsync(), () => !IsBusy && IsConnected);
        ApplyAllCommand = new AsyncRelayCommand(async () => await ApplyAllAsync(), () => !IsBusy && IsConnected);
        ResetDefaultsCommand = new AsyncRelayCommand(async () => await ResetDefaultsAsync(), () => !IsBusy && IsConnected);
    }

    private static IScannerService CreateDefaultScannerService()
    {
        // For single-file publish, extracted DLLs go to AppContext.BaseDirectory.
        // For normal builds, DLLs are next to the exe.
        string baseDir = AppContext.BaseDirectory;
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? baseDir) ?? baseDir;
        string dllPath = Path.Combine(baseDir, "Opticon.csp2Ex.net.dll");
        if (!File.Exists(dllPath))
            dllPath = Path.Combine(exeDir, "Opticon.csp2Ex.net.dll");
        bool useOpticon = File.Exists(dllPath);
        LogService.Debug("[ScannerSettingsViewModel] Using {Service}", useOpticon ? "OpticonScannerService" : "MockScannerService");
        if (useOpticon)
            return new OpticonScannerService();
        return new MockScannerService();
    }

    private void Disconnect()
    {
        try
        {
            _scannerService.Disconnect();
            LogService.Info("[ScannerSettingsViewModel] Disconnected from scanner");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ScannerSettingsViewModel] Disconnect failed");
        }
        finally
        {
            IsConnected = false;
            ScannerInfo = null;
            StatusChanged?.Invoke(this, "Disconnected from scanner.");
        }
    }

    public void HandleDeviceRemoved()
    {
        if (!IsConnected) return;

        try
        {
            var availablePorts = System.IO.Ports.SerialPort.GetPortNames();
            string ourPort = $"COM{SelectedPort}";

            if (!availablePorts.Contains(ourPort))
            {
                LogService.Warning("[ScannerSettingsViewModel] COM port {Port} disappeared - scanner was unplugged", ourPort);
                Disconnect();
                StatusChanged?.Invoke(this, $"Scanner disconnected - {ourPort} was removed.");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ScannerSettingsViewModel] Error checking COM port after device removal");
        }
    }

    public void Cleanup()
    {
        if (IsConnected)
            Disconnect();
    }

    private async Task DetectScannersAsync()
    {
        IsBusy = true;
        StatusChanged?.Invoke(this, "Detecting scanners...");
        try
        {
            var ports = await Task.Run(() => _scannerService.DetectScanners());
            AvailablePorts.Clear();
            foreach (var port in ports)
                AvailablePorts.Add(port);

            if (ports.Count > 0)
            {
                SelectedPort = ports[0];
                StatusChanged?.Invoke(this, $"Found {ports.Count} scanner(s). Selected COM{ports[0]}.");
            }
            else
            {
                string detail = (_scannerService is OpticonScannerService opticon && opticon.LastError != null)
                    ? $"No scanners detected. Detail: {opticon.LastError}"
                    : "No scanners detected.";
                StatusChanged?.Invoke(this, detail);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Detection failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAsync()
    {
        if (SelectedPort <= 0)
        {
            StatusChanged?.Invoke(this, "No COM port selected.");
            return;
        }

        IsBusy = true;
        StatusChanged?.Invoke(this, $"Connecting to scanner on COM{SelectedPort}...");
        try
        {
            bool connected = await Task.Run(() => _scannerService.Connect(SelectedPort));
            if (!connected)
            {
                StatusChanged?.Invoke(this, $"Failed to connect to COM{SelectedPort}.");
                return;
            }
            IsConnected = true;
            ScannerInfo = await Task.Run(() => _scannerService.GetScannerInfo());
            StatusChanged?.Invoke(this, $"Connected to {ScannerInfo.Model} on COM{SelectedPort}.");
            await ReadAllAsync();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Connect failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReadAllAsync()
    {
        IsBusy = true;
        StatusChanged?.Invoke(this, "Reading scanner parameters...");
        try
        {
            await Task.Run(() =>
            {
                var buf = new byte[1];

                if (_scannerService.GetParam(2, buf, 1) == 0)
                    BuzzerEnabled = buf[0] != 0;

                if (_scannerService.GetParam(4, buf, 1) == 0)
                    RejectRedundantIndex = Math.Clamp((int)buf[0], 0, 2);

                if (_scannerService.GetParam(7, buf, 1) == 0)
                    LowBatteryIndex = Math.Clamp((int)buf[0], 0, 3);

                if (_scannerService.GetParam(10, buf, 1) == 0)
                    HostConnectBeepEnabled = buf[0] != 0;

                if (_scannerService.GetParam(11, buf, 1) == 0)
                    HostCompleteBeepEnabled = buf[0] != 0;

                if (_scannerService.GetParam(17, buf, 1) == 0)
                    ScannerOnTime = buf[0].ToString();

                if (_scannerService.GetParam(34, buf, 1) == 0)
                    MaxBarcodeLength = buf[0].ToString();

                if (_scannerService.GetParam(35, buf, 1) == 0)
                    StoreTimestampEnabled = buf[0] != 0;
            });
            StatusChanged?.Invoke(this, "Scanner parameters read successfully.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Read failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAllAsync()
    {
        IsBusy = true;
        StatusChanged?.Invoke(this, "Applying scanner parameters...");
        try
        {
            await Task.Run(() =>
            {
                var buf = new byte[1];

                buf[0] = BuzzerEnabled ? (byte)1 : (byte)0;
                _scannerService.SetParam(2, buf, 1);

                buf[0] = (byte)RejectRedundantIndex;
                _scannerService.SetParam(4, buf, 1);

                buf[0] = (byte)LowBatteryIndex;
                _scannerService.SetParam(7, buf, 1);

                buf[0] = HostConnectBeepEnabled ? (byte)1 : (byte)0;
                _scannerService.SetParam(10, buf, 1);

                buf[0] = HostCompleteBeepEnabled ? (byte)1 : (byte)0;
                _scannerService.SetParam(11, buf, 1);

                if (byte.TryParse(ScannerOnTime, out byte onTime) && onTime >= 1)
                {
                    buf[0] = onTime;
                    _scannerService.SetParam(17, buf, 1);
                }

                if (byte.TryParse(MaxBarcodeLength, out byte maxLen) && maxLen >= 1 && maxLen <= 30)
                {
                    buf[0] = maxLen;
                    _scannerService.SetParam(34, buf, 1);
                }

                buf[0] = StoreTimestampEnabled ? (byte)1 : (byte)0;
                _scannerService.SetParam(35, buf, 1);
            });
            StatusChanged?.Invoke(this, "Scanner parameters applied successfully.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Apply failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetDefaultsAsync()
    {
        var result = MessageBox.Show(
            "Are you sure? This will reset all scanner settings to factory defaults.",
            "Confirm Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusChanged?.Invoke(this, "Resetting scanner to factory defaults...");
        try
        {
            await Task.Run(() => _scannerService.SetDefaults());
            StatusChanged?.Invoke(this, "Scanner reset to factory defaults.");
            await ReadAllAsync();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Reset failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        set => SetProperty(ref _selectedPort, value);
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

    public ICommand RefreshPortsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearDisplayCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand SaveCommand { get; }

    public BluetoothLiveViewModel() : this(new BluetoothScanService(), new FileExportService()) { }

    public BluetoothLiveViewModel(IBluetoothScanService bluetoothService, IFileExportService fileExportService)
    {
        _bluetoothService = bluetoothService;
        _fileExportService = fileExportService;
        _bluetoothService.BarcodeReceived += OnBarcodeReceived;

        _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        ConnectCommand = new RelayCommand(_ => Connect(), _ => !IsConnected && SelectedPort != null);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        ClearDisplayCommand = new RelayCommand(_ => ClearDisplay());
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => _barcodes.Count > 0);

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

    private void Disconnect()
    {
        _bluetoothService.Disconnect();
        IsConnected = false;
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

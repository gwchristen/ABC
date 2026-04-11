using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ABC.Helpers;
using ABC.Models;
using ABC.Services;

namespace ABC.ViewModels;

public class UsbDownloadViewModel : ViewModelBase
{

    private readonly IScannerService _scannerService;
    private readonly IFileExportService _fileExportService;

    private ScannerInfo? _scannerInfo;
    private ObservableCollection<int> _availablePorts = new();
    private int _selectedPort;
    private ObservableCollection<BarcodeEntry> _barcodes = new();
    private string _saveDirectory = string.Empty;
    private string _fileName = GenerateDefaultFileName();
    private bool _saveAsCsv;
    private bool _clearAfterSave;
    private bool _isBusy;
    private bool _isConnected;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? BarcodeCountChanged;

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

    public ObservableCollection<BarcodeEntry> Barcodes
    {
        get => _barcodes;
        private set => SetProperty(ref _barcodes, value);
    }

    public int BarcodeCount => _barcodes.Count;

    public int DuplicateCount => _barcodes.Count(b => b.IsDuplicate);

    public bool HasDuplicates => DuplicateCount > 0;

    public int InvalidLengthCount => _barcodes.Count(b => b.IsInvalidLength);

    public bool HasInvalidLength => InvalidLengthCount > 0;

    public int SelectedCount => _barcodes.Count(b => b.IsSelected);

    public bool HasSelected => SelectedCount > 0;

    public bool? IsAllSelected
    {
        get
        {
            if (_barcodes.Count == 0) return false;
            int selected = _barcodes.Count(b => b.IsSelected);
            if (selected == 0) return false;
            if (selected == _barcodes.Count) return true;
            return null;
        }
        set
        {
            bool newValue = value ?? false;
            foreach (var b in _barcodes)
                b.IsSelected = newValue;
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelected));
            CommandManager.InvalidateRequerySuggested();
        }
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

    public bool ClearAfterSave
    {
        get => _clearAfterSave;
        set => SetProperty(ref _clearAfterSave, value);
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

    public ICommand DetectScannersCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ClearScannerDataCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand BrowseAppendFileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand ClearSelectionCommand { get; }

    public UsbDownloadViewModel() : this(CreateDefaultScannerService(), new FileExportService()) { }

    public UsbDownloadViewModel(IScannerService scannerService, IFileExportService fileExportService)
    {
        _scannerService = scannerService;
        _fileExportService = fileExportService;

        _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        DetectScannersCommand = new AsyncRelayCommand(async () => await DetectScannersAsync(), () => !IsBusy);
        PreviewCommand = new AsyncRelayCommand(async () => await PreviewAsync(), () => !IsBusy && SelectedPort > 0);
        ClearScannerDataCommand = new AsyncRelayCommand(async () => await ClearScannerDataAsync(), () => !IsBusy && IsConnected);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        BrowseAppendFileCommand = new RelayCommand(_ => BrowseAppendFile());
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync(), () => !IsBusy && Barcodes.Count > 0);
        RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => !IsBusy && SelectedCount > 0);
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => SelectedCount > 0);
    }

    private static IScannerService CreateDefaultScannerService()
    {
        // Use the real service if the Opticon DLL is present, otherwise fall back to mock
        // For single-file publish, extracted DLLs go to AppContext.BaseDirectory.
        // For normal builds, DLLs are next to the exe.
        string baseDir = AppContext.BaseDirectory;
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? baseDir) ?? baseDir;
        string dllPath = Path.Combine(baseDir, "Opticon.csp2Ex.net.dll");
        if (!File.Exists(dllPath))
            dllPath = Path.Combine(exeDir, "Opticon.csp2Ex.net.dll");
        bool useOpticon = File.Exists(dllPath);
        LogService.Debug("[UsbDownloadViewModel] Using {Service}", useOpticon ? "OpticonScannerService" : "MockScannerService");
        if (useOpticon)
            return new OpticonScannerService();
        return new MockScannerService();
    }

    private void Disconnect()
    {
        try
        {
            _scannerService.Disconnect();
            LogService.Info("[UsbDownloadViewModel] Disconnected from scanner");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UsbDownloadViewModel] Disconnect failed");
        }
        finally
        {
            IsConnected = false;
            ScannerInfo = null;
            foreach (var b in _barcodes.ToList())
                UnsubscribeFromBarcode(b);
            Barcodes.Clear();
            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(DuplicateCount));
            OnPropertyChanged(nameof(HasDuplicates));
            OnPropertyChanged(nameof(InvalidLengthCount));
            OnPropertyChanged(nameof(HasInvalidLength));
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelected));
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
                LogService.Warning("[UsbDownloadViewModel] COM port {Port} disappeared - scanner was unplugged", ourPort);
                Disconnect();
                StatusChanged?.Invoke(this, $"Scanner disconnected - {ourPort} was removed.");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UsbDownloadViewModel] Error checking COM port after device removal");
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
        LogService.Info("[UsbDownloadViewModel] Detecting scanners");
        try
        {
            var ports = await Task.Run(() => _scannerService.DetectScanners());
            AvailablePorts.Clear();
            foreach (var port in ports)
                AvailablePorts.Add(port);

            if (ports.Count > 0)
            {
                SelectedPort = ports[0];
                LogService.Info("[UsbDownloadViewModel] Found {Count} scanner(s)", ports.Count);
                StatusChanged?.Invoke(this, $"Found {ports.Count} scanner(s). Selected COM{ports[0]}.");
            }
            else
            {
                string detail = (_scannerService is OpticonScannerService opticon && opticon.LastError != null)
                    ? $"No scanners detected. Detail: {opticon.LastError}"
                    : "No scanners detected.";
                LogService.Warning("[UsbDownloadViewModel] {Detail}", detail);
                StatusChanged?.Invoke(this, detail);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UsbDownloadViewModel] Detection failed");
            StatusChanged?.Invoke(this, $"Detection failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewAsync()
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
            StatusChanged?.Invoke(this, $"Reading barcodes from {ScannerInfo.Model}...");

            var barcodes = await Task.Run(() => _scannerService.ReadAllBarcodes());

            var duplicateValues = barcodes
                .GroupBy(b => b.Barcode)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            foreach (var b in barcodes)
                b.IsDuplicate = duplicateValues.Contains(b.Barcode);

            foreach (var b in barcodes)
                b.IsInvalidLength = b.Barcode.Length != BarcodeParser.ExpectedLength;

            foreach (var b in _barcodes.ToList())
                UnsubscribeFromBarcode(b);
            Barcodes.Clear();
            foreach (var b in barcodes)
            {
                SubscribeToBarcode(b);
                Barcodes.Add(b);
            }

            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(DuplicateCount));
            OnPropertyChanged(nameof(HasDuplicates));
            OnPropertyChanged(nameof(InvalidLengthCount));
            OnPropertyChanged(nameof(HasInvalidLength));
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelected));
            StatusChanged?.Invoke(this, $"Downloaded {barcodes.Count} barcode(s) from {ScannerInfo.Model}.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Preview failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearScannerDataAsync()
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear all barcode data from the scanner? This cannot be undone.",
            "Confirm Clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusChanged?.Invoke(this, "Clearing scanner data...");
        try
        {
            bool cleared = await Task.Run(() => _scannerService.ClearScannerData());
            if (cleared)
            {
                foreach (var b in _barcodes.ToList())
                    UnsubscribeFromBarcode(b);
                Barcodes.Clear();
                BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(DuplicateCount));
                OnPropertyChanged(nameof(HasDuplicates));
                OnPropertyChanged(nameof(InvalidLengthCount));
                OnPropertyChanged(nameof(HasInvalidLength));
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(HasSelected));
                StatusChanged?.Invoke(this, "Scanner data cleared successfully.");
            }
            else
            {
                StatusChanged?.Invoke(this, "Failed to clear scanner data.");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Clear failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
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

            IsBusy = true;
            StatusChanged?.Invoke(this, $"Appending to {AppendFilePath}...");
            try
            {
                bool saved = await Task.Run(() => isCsv
                    ? _fileExportService.AppendAsCsv(AppendFilePath, Barcodes)
                    : _fileExportService.AppendAsText(AppendFilePath, Barcodes));

                if (saved)
                {
                    LogService.Info("[UsbDownloadViewModel] Appended {Count} barcodes to {Path}", Barcodes.Count, AppendFilePath);
                    StatusChanged?.Invoke(this, $"Appended {Barcodes.Count} barcode(s) to {AppendFilePath}.");
                    if (ClearAfterSave)
                        await ClearScannerDataAsync();
                }
                else
                {
                    LogService.Warning("[UsbDownloadViewModel] Append failed for {Path}", AppendFilePath);
                    StatusChanged?.Invoke(this, "Failed to save file.");
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[UsbDownloadViewModel] Append failed");
                StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
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

        if (File.Exists(fullPath))
        {
            var confirm = MessageBox.Show(
                "File already exists. Do you want to overwrite it?",
                "Confirm Overwrite",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusChanged?.Invoke(this, "Save cancelled.");
                return;
            }
        }

        IsBusy = true;
        StatusChanged?.Invoke(this, $"Saving to {fullPath}...");
        try
        {
            bool saved = await Task.Run(() => SaveAsCsv
                ? _fileExportService.SaveAsCsv(fullPath, Barcodes)
                : _fileExportService.SaveAsText(fullPath, Barcodes));

            if (saved)
            {
                LogService.Info("[UsbDownloadViewModel] Saved {Count} barcodes to {Path}", Barcodes.Count, fullPath);
                StatusChanged?.Invoke(this, $"Saved {Barcodes.Count} barcode(s) to {fullPath}.");
                if (ClearAfterSave)
                    await ClearScannerDataAsync();
            }
            else
            {
                LogService.Warning("[UsbDownloadViewModel] Save failed for {Path}", fullPath);
                StatusChanged?.Invoke(this, "Failed to save file.");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UsbDownloadViewModel] Save failed");
            StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveSelected()
    {
        var toRemove = _barcodes.Where(b => b.IsSelected).ToList();
        foreach (var item in toRemove)
        {
            UnsubscribeFromBarcode(item);
            Barcodes.Remove(item);
        }

        for (int i = 0; i < Barcodes.Count; i++)
            Barcodes[i].SequenceNumber = i + 1;

        var duplicateValues = _barcodes
            .GroupBy(b => b.Barcode)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        foreach (var b in _barcodes)
            b.IsDuplicate = duplicateValues.Contains(b.Barcode);

        foreach (var b in _barcodes)
            b.IsInvalidLength = b.Barcode.Length != BarcodeParser.ExpectedLength;

        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(InvalidLengthCount));
        OnPropertyChanged(nameof(HasInvalidLength));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelected));
    }

    private void ClearSelection()
    {
        foreach (var b in _barcodes)
            b.IsSelected = false;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(HasSelected));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SubscribeToBarcode(BarcodeEntry entry)
        => entry.PropertyChanged += OnBarcodePropertyChanged;

    private void UnsubscribeFromBarcode(BarcodeEntry entry)
        => entry.PropertyChanged -= OnBarcodePropertyChanged;

    private void OnBarcodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BarcodeEntry.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(HasSelected));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static string GenerateDefaultFileName()
        => $"scan_{DateTime.Now:yyyyMMdd_HHmmss}";
}

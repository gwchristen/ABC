using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
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
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand SaveCommand { get; }

    public UsbDownloadViewModel() : this(CreateDefaultScannerService(), new FileExportService()) { }

    public UsbDownloadViewModel(IScannerService scannerService, IFileExportService fileExportService)
    {
        _scannerService = scannerService;
        _fileExportService = fileExportService;

        _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        DetectScannersCommand = new RelayCommand(async _ => await DetectScannersAsync(), _ => !IsBusy);
        PreviewCommand = new RelayCommand(async _ => await PreviewAsync(), _ => !IsBusy && SelectedPort > 0);
        ClearScannerDataCommand = new RelayCommand(async _ => await ClearScannerDataAsync(), _ => !IsBusy && IsConnected);
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsBusy && Barcodes.Count > 0);
    }

    private static IScannerService CreateDefaultScannerService()
    {
        // Use the real service if the Opticon DLL is present, otherwise fall back to mock
        string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Opticon.csp2.net.dll");
        bool useOpticon = File.Exists(dllPath);
        System.Diagnostics.Debug.WriteLine($"[UsbDownloadViewModel] Using {(useOpticon ? "OpticonScannerService" : "MockScannerService")}");
        if (useOpticon)
            return new OpticonScannerService();
        return new MockScannerService();
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

            Barcodes.Clear();
            foreach (var b in barcodes)
                Barcodes.Add(b);

            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(DuplicateCount));
            OnPropertyChanged(nameof(HasDuplicates));
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
                Barcodes.Clear();
                BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(DuplicateCount));
                OnPropertyChanged(nameof(HasDuplicates));
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

        IsBusy = true;
        StatusChanged?.Invoke(this, $"Saving to {fullPath}...");
        try
        {
            bool saved = await Task.Run(() =>
                SaveAsCsv
                    ? _fileExportService.SaveAsCsv(fullPath, Barcodes)
                    : _fileExportService.SaveAsText(fullPath, Barcodes));

            if (saved)
            {
                StatusChanged?.Invoke(this, $"Saved {Barcodes.Count} barcode(s) to {fullPath}.");
                if (ClearAfterSave)
                    await ClearScannerDataAsync();
            }
            else
            {
                StatusChanged?.Invoke(this, "Failed to save file.");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GenerateDefaultFileName()
        => $"scan_{DateTime.Now:yyyyMMdd_HHmmss}";
}

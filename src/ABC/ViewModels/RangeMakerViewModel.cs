using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ABC.Helpers;
using ABC.Models;
using ABC.Services;

namespace ABC.ViewModels;

public class RangeMakerViewModel : ViewModelBase
{
    private readonly IScannerService _scannerService;

    private ScannerInfo? _scannerInfo;
    private ObservableCollection<int> _availablePorts = new();
    private int _selectedPort;
    private ObservableCollection<BarcodeEntry> _barcodes = new();
    private ObservableCollection<RangeEntry> _generatedRange = new();
    private bool _isBusy;
    private bool _isConnected;
    private bool _isFirstOnlyMode = true;
    private int _rangeQuantity = 50;
    private string _saveDirectory = string.Empty;
    private string _fileName = GenerateDefaultFileName();
    private bool _saveAsCsv;
    private bool _appendToFile;
    private string _appendFilePath = string.Empty;

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
            if (value == null) return;
            bool newValue = value.Value;
            foreach (var b in _barcodes)
                b.IsSelected = newValue;
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelected));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ObservableCollection<RangeEntry> GeneratedRange
    {
        get => _generatedRange;
        private set => SetProperty(ref _generatedRange, value);
    }

    public int GeneratedRangeCount => _generatedRange.Count;

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

    public bool IsFirstOnlyMode
    {
        get => _isFirstOnlyMode;
        set
        {
            SetProperty(ref _isFirstOnlyMode, value);
            OnPropertyChanged(nameof(IsFirstLastMode));
            OnPropertyChanged(nameof(RangeSizeDisplay));
        }
    }

    public bool IsFirstLastMode
    {
        get => !_isFirstOnlyMode;
        set
        {
            IsFirstOnlyMode = !value;
        }
    }

    public int RangeQuantity
    {
        get => _rangeQuantity;
        set => SetProperty(ref _rangeQuantity, value < 1 ? 1 : value);
    }

    public string RangeSizeDisplay
    {
        get
        {
            if (!IsFirstLastMode) return string.Empty;

            var selected = _barcodes.Where(b => b.IsSelected).ToList();
            if (selected.Count != 2) return string.Empty;

            if (!BarcodeParser.TryParse(selected[0].Barcode, out _, out long serial1, out _))
                return string.Empty;
            if (!BarcodeParser.TryParse(selected[1].Barcode, out _, out long serial2, out _))
                return string.Empty;

            long size = Math.Abs(serial2 - serial1) + 1;
            return $"Range size: {size}";
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

    public bool AppendToFile
    {
        get => _appendToFile;
        set => SetProperty(ref _appendToFile, value);
    }

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

    public bool ShowRangeSizeDisplay
    {
        get
        {
            if (!IsFirstLastMode) return false;
            return _barcodes.Count(b => b.IsSelected) == 2;
        }
    }

    public ICommand DetectScannersCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ClearScannerDataCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand GenerateRangeCommand { get; }
    public ICommand ClearPreviewCommand { get; }
    public ICommand ClearRangeCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand BrowseAppendFileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand AddManualBarcodeCommand { get; }

    public RangeMakerViewModel() : this(CreateDefaultScannerService()) { }

    public RangeMakerViewModel(IScannerService scannerService)
    {
        _scannerService = scannerService;
        _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        DetectScannersCommand = new AsyncRelayCommand(async () => await DetectScannersAsync(), () => !IsBusy);
        PreviewCommand = new AsyncRelayCommand(async () => await PreviewAsync(), () => !IsBusy && SelectedPort > 0);
        ClearScannerDataCommand = new AsyncRelayCommand(async () => await ClearScannerDataAsync(), () => !IsBusy && IsConnected);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        GenerateRangeCommand = new RelayCommand(_ => GenerateRange(), _ => !IsBusy);
        ClearPreviewCommand = new RelayCommand(_ => ClearPreview());
        ClearRangeCommand = new RelayCommand(_ => ClearRange());
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        BrowseAppendFileCommand = new RelayCommand(_ => BrowseAppendFile());
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync(), () => !IsBusy && GeneratedRange.Count > 0);
        RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => !IsBusy && SelectedCount > 0);
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => SelectedCount > 0);
        AddManualBarcodeCommand = new RelayCommand(_ => AddManualBarcode());
    }

    private static IScannerService CreateDefaultScannerService()
    {
        string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Opticon.csp2.net.dll");
        bool useOpticon = File.Exists(dllPath);
        LogService.Debug("[RangeMakerViewModel] Using {Service}", useOpticon ? "OpticonScannerService" : "MockScannerService");
        if (useOpticon)
            return new OpticonScannerService();
        return new MockScannerService();
    }

    private void Disconnect()
    {
        try
        {
            _scannerService.Disconnect();
            LogService.Info("[RangeMakerViewModel] Disconnected from scanner");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[RangeMakerViewModel] Disconnect failed");
        }
        finally
        {
            IsConnected = false;
            ScannerInfo = null;
            foreach (var b in _barcodes.ToList())
                UnsubscribeFromBarcode(b);
            Barcodes.Clear();
            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelected));
            OnPropertyChanged(nameof(RangeSizeDisplay));
            OnPropertyChanged(nameof(ShowRangeSizeDisplay));
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
                LogService.Warning("[RangeMakerViewModel] COM port {Port} disappeared - scanner was unplugged", ourPort);
                Disconnect();
                StatusChanged?.Invoke(this, $"Scanner disconnected - {ourPort} was removed.");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[RangeMakerViewModel] Error checking COM port after device removal");
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
        LogService.Info("[RangeMakerViewModel] Detecting scanners");
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

            foreach (var b in _barcodes.ToList())
                UnsubscribeFromBarcode(b);
            Barcodes.Clear();
            foreach (var b in barcodes)
            {
                SubscribeToBarcode(b);
                Barcodes.Add(b);
            }

            BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelected));
            OnPropertyChanged(nameof(RangeSizeDisplay));
            OnPropertyChanged(nameof(ShowRangeSizeDisplay));
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
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(HasSelected));
                OnPropertyChanged(nameof(RangeSizeDisplay));
                OnPropertyChanged(nameof(ShowRangeSizeDisplay));
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

    private void GenerateRange()
    {
        if (IsFirstOnlyMode)
        {
            var selected = _barcodes.Where(b => b.IsSelected).ToList();
            if (selected.Count != 1)
            {
                StatusChanged?.Invoke(this, "Please select exactly 1 barcode for 'First barcode only' mode.");
                return;
            }

            if (RangeQuantity < 1)
            {
                StatusChanged?.Invoke(this, "Quantity must be at least 1.");
                return;
            }

            if (!BarcodeParser.TryParse(selected[0].Barcode, out string prefix, out long startSerial, out string suffix))
            {
                StatusChanged?.Invoke(this, "Selected barcode does not match expected 17-character format.");
                return;
            }

            if (startSerial + RangeQuantity - 1 > 999_999_999L)
            {
                StatusChanged?.Invoke(this, "Range would overflow 9-digit serial number limit.");
                return;
            }

            int startIndex = _generatedRange.Count + 1;
            for (int i = 0; i < RangeQuantity; i++)
            {
                _generatedRange.Add(new RangeEntry
                {
                    SequenceNumber = startIndex + i,
                    Barcode = BarcodeParser.Build(prefix, startSerial + i, suffix)
                });
            }

            OnPropertyChanged(nameof(GeneratedRangeCount));
            CommandManager.InvalidateRequerySuggested();
            StatusChanged?.Invoke(this, $"Generated {RangeQuantity} barcode(s) in range.");
        }
        else
        {
            var selected = _barcodes.Where(b => b.IsSelected).ToList();
            if (selected.Count != 2)
            {
                StatusChanged?.Invoke(this, "Please select exactly 2 barcodes for 'First + Last barcode' mode.");
                return;
            }

            if (!BarcodeParser.TryParse(selected[0].Barcode, out string prefix1, out long serial1, out string suffix1))
            {
                StatusChanged?.Invoke(this, "First selected barcode does not match expected 17-character format.");
                return;
            }

            if (!BarcodeParser.TryParse(selected[1].Barcode, out string prefix2, out long serial2, out string suffix2))
            {
                StatusChanged?.Invoke(this, "Second selected barcode does not match expected 17-character format.");
                return;
            }

            if (prefix1 != prefix2 || suffix1 != suffix2)
            {
                StatusChanged?.Invoke(this, "Selected barcodes must have the same prefix and suffix.");
                return;
            }

            long startSerial = Math.Min(serial1, serial2);
            long endSerial = Math.Max(serial1, serial2);
            long count = endSerial - startSerial + 1;

            int startIndex = _generatedRange.Count + 1;
            if (count > int.MaxValue - startIndex + 1)
            {
                StatusChanged?.Invoke(this, "Range is too large to generate (exceeds maximum sequence number).");
                return;
            }

            for (long i = 0; i < count; i++)
            {
                _generatedRange.Add(new RangeEntry
                {
                    SequenceNumber = (int)(startIndex + i),
                    Barcode = BarcodeParser.Build(prefix1, startSerial + i, suffix1)
                });
            }

            OnPropertyChanged(nameof(GeneratedRangeCount));
            CommandManager.InvalidateRequerySuggested();
            StatusChanged?.Invoke(this, $"Generated {count} barcode(s) in range.");
        }
    }

    private void ClearPreview()
    {
        foreach (var b in _barcodes.ToList())
            UnsubscribeFromBarcode(b);
        Barcodes.Clear();
        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(RangeSizeDisplay));
        OnPropertyChanged(nameof(ShowRangeSizeDisplay));
    }

    private void ClearRange()
    {
        GeneratedRange.Clear();
        OnPropertyChanged(nameof(GeneratedRangeCount));
        CommandManager.InvalidateRequerySuggested();
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
                var barcodes = _generatedRange.Select(r => r.Barcode).ToList();
                bool saved = await Task.Run(() => isCsv
                    ? AppendRangeAsCsv(AppendFilePath, barcodes)
                    : AppendRangeAsText(AppendFilePath, barcodes));

                if (saved)
                {
                    LogService.Info("[RangeMakerViewModel] Appended {Count} barcodes to {Path}", _generatedRange.Count, AppendFilePath);
                    StatusChanged?.Invoke(this, $"Appended {_generatedRange.Count} barcode(s) to {AppendFilePath}.");
                }
                else
                {
                    LogService.Warning("[RangeMakerViewModel] Append failed for {Path}", AppendFilePath);
                    StatusChanged?.Invoke(this, "Failed to save file.");
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[RangeMakerViewModel] Append failed");
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
            var barcodes = _generatedRange.Select(r => r.Barcode).ToList();
            bool saved = await Task.Run(() => SaveAsCsv
                ? SaveRangeAsCsv(fullPath, barcodes)
                : SaveRangeAsText(fullPath, barcodes));

            if (saved)
            {
                LogService.Info("[RangeMakerViewModel] Saved {Count} barcodes to {Path}", _generatedRange.Count, fullPath);
                StatusChanged?.Invoke(this, $"Saved {_generatedRange.Count} barcode(s) to {fullPath}.");
            }
            else
            {
                LogService.Warning("[RangeMakerViewModel] Save failed for {Path}", fullPath);
                StatusChanged?.Invoke(this, "Failed to save file.");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[RangeMakerViewModel] Save failed");
            StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool SaveRangeAsText(string filePath, IEnumerable<string> barcodes)
    {
        File.WriteAllLines(filePath, barcodes);
        return true;
    }

    private static bool SaveRangeAsCsv(string filePath, IEnumerable<string> barcodes)
    {
        using var writer = new StreamWriter(filePath, append: false);
        writer.WriteLine("Barcode");
        foreach (var b in barcodes)
            writer.WriteLine(b);
        return true;
    }

    private static bool AppendRangeAsText(string filePath, IEnumerable<string> barcodes)
    {
        File.AppendAllLines(filePath, barcodes);
        return true;
    }

    private static bool AppendRangeAsCsv(string filePath, IEnumerable<string> barcodes)
    {
        bool fileExists = File.Exists(filePath) && new FileInfo(filePath).Length > 0;
        using var writer = new StreamWriter(filePath, append: true);
        if (!fileExists)
            writer.WriteLine("Barcode");
        foreach (var b in barcodes)
            writer.WriteLine(b);
        return true;
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

        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(RangeSizeDisplay));
        OnPropertyChanged(nameof(ShowRangeSizeDisplay));
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

    private void AddManualBarcode()
    {
        var dialog = new Views.InputDialog();
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            string barcode = dialog.InputText.Trim();
            if (!string.IsNullOrEmpty(barcode))
            {
                var entry = new BarcodeEntry
                {
                    Barcode = barcode,
                    Timestamp = DateTime.Now,
                    CodeType = "Manual",
                    ScannerId = "Manual Entry",
                    SequenceNumber = Barcodes.Count + 1
                };
                SubscribeToBarcode(entry);
                Barcodes.Add(entry);
                BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(HasSelected));
                OnPropertyChanged(nameof(RangeSizeDisplay));
                OnPropertyChanged(nameof(ShowRangeSizeDisplay));
                StatusChanged?.Invoke(this, $"Manually added barcode: {barcode}");
            }
        }
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
            OnPropertyChanged(nameof(RangeSizeDisplay));
            OnPropertyChanged(nameof(ShowRangeSizeDisplay));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static string GenerateDefaultFileName()
        => $"range_{DateTime.Now:yyyyMMdd_HHmmss}";
}

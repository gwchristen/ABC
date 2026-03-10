using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ABC.Helpers;
using ABC.Models;
using ABC.Services;

namespace ABC.ViewModels;

public class NotepadScanViewModel : ViewModelBase
{
    private readonly IFileExportService _fileExportService;

    private ObservableCollection<BarcodeEntry> _barcodes = new();
    private string _scanInput = string.Empty;
    private string _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _fileName = GenerateDefaultFileName();
    private bool _saveAsCsv;
    private bool _appendToFile;
    private string _appendFilePath = string.Empty;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? BarcodeCountChanged;

    public ObservableCollection<BarcodeEntry> Barcodes
    {
        get => _barcodes;
        private set => SetProperty(ref _barcodes, value);
    }

    public string ScanInput
    {
        get => _scanInput;
        set => SetProperty(ref _scanInput, value);
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
            if (SetProperty(ref _appendFilePath, value))
                OnPropertyChanged(nameof(AppendFileName));
        }
    }

    public string AppendFileName => Path.GetFileName(_appendFilePath);

    public ICommand ClearCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand BrowseAppendFileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand ClearSelectionCommand { get; }

    public NotepadScanViewModel() : this(new FileExportService()) { }

    public NotepadScanViewModel(IFileExportService fileExportService)
    {
        _fileExportService = fileExportService;

        ClearCommand = new RelayCommand(_ => ClearAll());
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        BrowseAppendFileCommand = new RelayCommand(_ => BrowseAppendFile());
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync(), () => _barcodes.Count > 0);
        RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedCount > 0);
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => SelectedCount > 0);
    }

    public void Cleanup() { }

    public void AddBarcode(string barcode)
    {
        var trimmed = barcode.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        var entry = new BarcodeEntry
        {
            Barcode = trimmed,
            Timestamp = DateTime.Now,
            SequenceNumber = _barcodes.Count + 1
        };
        SubscribeToBarcode(entry);
        _barcodes.Add(entry);

        RecalculateDuplicatesAndInvalidLengths();

        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(BarcodeCount));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(InvalidLengthCount));
        OnPropertyChanged(nameof(HasInvalidLength));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearAll()
    {
        foreach (var b in _barcodes.ToList())
            UnsubscribeFromBarcode(b);
        _barcodes.Clear();

        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(BarcodeCount));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(InvalidLengthCount));
        OnPropertyChanged(nameof(HasInvalidLength));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelected));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveSelected()
    {
        var toRemove = _barcodes.Where(b => b.IsSelected).ToList();
        foreach (var item in toRemove)
        {
            UnsubscribeFromBarcode(item);
            _barcodes.Remove(item);
        }

        for (int i = 0; i < _barcodes.Count; i++)
            _barcodes[i].SequenceNumber = i + 1;

        RecalculateDuplicatesAndInvalidLengths();

        BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(BarcodeCount));
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

    private void RecalculateDuplicatesAndInvalidLengths()
    {
        var duplicateValues = _barcodes
            .GroupBy(b => b.Barcode)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var b in _barcodes)
            b.IsDuplicate = duplicateValues.Contains(b.Barcode);

        foreach (var b in _barcodes)
            b.IsInvalidLength = b.Barcode.Length != BarcodeParser.ExpectedLength;
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
        if (_barcodes.Count == 0)
        {
            StatusChanged?.Invoke(this, "Nothing to save.");
            return;
        }

        var entries = _barcodes.ToList();

        if (AppendToFile)
        {
            if (string.IsNullOrWhiteSpace(AppendFilePath))
            {
                StatusChanged?.Invoke(this, "Please select a file to append to.");
                return;
            }

            bool isCsv = string.Equals(Path.GetExtension(AppendFilePath), ".csv",
                StringComparison.OrdinalIgnoreCase);

            StatusChanged?.Invoke(this, $"Appending to {AppendFilePath}...");
            try
            {
                bool saved = await Task.Run(() => isCsv
                    ? _fileExportService.AppendAsCsv(AppendFilePath, entries)
                    : _fileExportService.AppendAsText(AppendFilePath, entries));

                StatusChanged?.Invoke(this, saved
                    ? $"Appended {entries.Count} barcode(s) to {AppendFilePath}."
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

        StatusChanged?.Invoke(this, $"Saving to {fullPath}...");
        try
        {
            bool saved = await Task.Run(() => SaveAsCsv
                ? _fileExportService.SaveAsCsv(fullPath, entries)
                : _fileExportService.SaveAsText(fullPath, entries));

            StatusChanged?.Invoke(this, saved
                ? $"Saved {entries.Count} barcode(s) to {fullPath}."
                : "Failed to save file.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
        }
    }

    private static string GenerateDefaultFileName()
        => $"notepad_{DateTime.Now:yyyyMMdd_HHmmss}";
}

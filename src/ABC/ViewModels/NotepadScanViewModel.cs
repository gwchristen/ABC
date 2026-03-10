using System.IO;
using System.Windows.Input;
using ABC.Helpers;
using ABC.Models;
using ABC.Services;

namespace ABC.ViewModels;

public class NotepadScanViewModel : ViewModelBase
{
    private readonly IFileExportService _fileExportService;

    private string _notepadText = string.Empty;
    private string _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _fileName = GenerateDefaultFileName();
    private bool _saveAsCsv;
    private bool _appendToFile;
    private string _appendFilePath = string.Empty;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? BarcodeCountChanged;

    public string NotepadText
    {
        get => _notepadText;
        set
        {
            if (SetProperty(ref _notepadText, value))
            {
                OnPropertyChanged(nameof(LineCount));
                OnPropertyChanged(nameof(BarcodeCount));
                OnPropertyChanged(nameof(DuplicateCount));
                OnPropertyChanged(nameof(HasDuplicates));
                OnPropertyChanged(nameof(InvalidLengthCount));
                OnPropertyChanged(nameof(HasInvalidLength));
                BarcodeCountChanged?.Invoke(this, EventArgs.Empty);
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public int LineCount => GetNonEmptyLines().Count;

    public int BarcodeCount => LineCount;

    public int DuplicateCount
    {
        get
        {
            var lines = GetNonEmptyLines();
            return lines.GroupBy(l => l, StringComparer.Ordinal)
                        .Count(g => g.Count() > 1) > 0
                ? lines.Count - lines.Distinct(StringComparer.Ordinal).Count()
                : 0;
        }
    }

    public bool HasDuplicates => DuplicateCount > 0;

    public int InvalidLengthCount =>
        GetNonEmptyLines().Count(l => l.Length != BarcodeParser.ExpectedLength);

    public bool HasInvalidLength => InvalidLengthCount > 0;

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

    public NotepadScanViewModel() : this(new FileExportService()) { }

    public NotepadScanViewModel(IFileExportService fileExportService)
    {
        _fileExportService = fileExportService;

        ClearCommand = new RelayCommand(_ => NotepadText = string.Empty);
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
        BrowseAppendFileCommand = new RelayCommand(_ => BrowseAppendFile());
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync(), () => LineCount > 0);
    }

    public void Cleanup() { }

    private List<string> GetNonEmptyLines()
        => _notepadText
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

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
        var lines = GetNonEmptyLines();
        if (lines.Count == 0)
        {
            StatusChanged?.Invoke(this, "Nothing to save.");
            return;
        }

        var entries = lines.Select(l => new BarcodeEntry
        {
            Barcode = l,
            Timestamp = DateTime.Now
        }).ToList();

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

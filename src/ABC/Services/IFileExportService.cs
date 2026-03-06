using ABC.Models;

namespace ABC.Services;

public interface IFileExportService
{
    bool SaveAsText(string filePath, IEnumerable<BarcodeEntry> barcodes);
    bool SaveAsCsv(string filePath, IEnumerable<BarcodeEntry> barcodes);
    bool AppendAsText(string filePath, IEnumerable<BarcodeEntry> barcodes);
    bool AppendAsCsv(string filePath, IEnumerable<BarcodeEntry> barcodes);
}

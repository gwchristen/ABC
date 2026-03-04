using System.IO;
using System.Text;
using ABC.Models;

namespace ABC.Services;

public class FileExportService : IFileExportService
{
    public bool SaveAsText(string filePath, IEnumerable<BarcodeEntry> barcodes)
    {
        try
        {
            var lines = barcodes.Select(b => b.Barcode);
            File.WriteAllLines(filePath, lines, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SaveAsCsv(string filePath, IEnumerable<BarcodeEntry> barcodes)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Barcode,CodeType,Timestamp,ScannerID");
            foreach (var entry in barcodes)
            {
                string timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                sb.AppendLine($"{EscapeCsv(entry.Barcode)},{EscapeCsv(entry.CodeType)},{timestamp},{EscapeCsv(entry.ScannerId)}");
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

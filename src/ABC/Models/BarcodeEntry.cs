namespace ABC.Models;

public class BarcodeEntry
{
    public string Barcode { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string CodeType { get; set; } = string.Empty;
    public string ScannerId { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
}

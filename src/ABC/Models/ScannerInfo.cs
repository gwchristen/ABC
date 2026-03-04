namespace ABC.Models;

public class ScannerInfo
{
    public string Model { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string BatteryStatus { get; set; } = string.Empty;
    public int BarcodeCount { get; set; }
    public int ProtocolVersion { get; set; }
    public int ComPort { get; set; }
}

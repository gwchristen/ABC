using ABC.Models;

namespace ABC.Services;

/// <summary>
/// Mock implementation of IScannerService for development and testing without hardware.
/// Returns sample data to allow UI development and testing.
/// </summary>
public class MockScannerService : IScannerService
{
    private bool _isConnected;
    private int _connectedPort;

    public bool IsConnected => _isConnected;

    public List<int> DetectScanners()
    {
        // Simulate finding a scanner on COM3
        return new List<int> { 3 };
    }

    public bool Connect(int comPort)
    {
        _connectedPort = comPort;
        _isConnected = true;
        return true;
    }

    public void Disconnect()
    {
        _isConnected = false;
        _connectedPort = 0;
    }

    public ScannerInfo GetScannerInfo()
    {
        if (!_isConnected)
            throw new InvalidOperationException("Scanner is not connected.");

        return new ScannerInfo
        {
            Model = "OPN-2002 (Mock)",
            FirmwareVersion = "1.23",
            SerialNumber = "MOCK001234",
            BatteryStatus = "75%",
            BarcodeCount = 3,
            ProtocolVersion = 2,
            ComPort = _connectedPort
        };
    }

    public List<BarcodeEntry> ReadAllBarcodes()
    {
        if (!_isConnected)
            throw new InvalidOperationException("Scanner is not connected.");

        var now = DateTime.Now;
        return new List<BarcodeEntry>
        {
            new() { Barcode = "0123456789012", Timestamp = now.AddMinutes(-5), CodeType = "EAN-13", ScannerId = "MOCK001234", SequenceNumber = 1 },
            new() { Barcode = "9876543210987", Timestamp = now.AddMinutes(-3), CodeType = "EAN-13", ScannerId = "MOCK001234", SequenceNumber = 2 },
            new() { Barcode = "5555555555555", Timestamp = now.AddMinutes(-1), CodeType = "Code128", ScannerId = "MOCK001234", SequenceNumber = 3 }
        };
    }

    public bool ClearScannerData()
    {
        if (!_isConnected)
            throw new InvalidOperationException("Scanner is not connected.");

        return true;
    }
}

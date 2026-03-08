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

    private readonly Dictionary<int, byte> _mockParams = new()
    {
        { 2, 1 },   // Buzzer on
        { 4, 0 },   // Reject redundant off
        { 7, 3 },   // Low-battery LED+Buzzer
        { 10, 1 },  // Host connect beep on
        { 11, 1 },  // Host complete beep on
        { 17, 10 }, // Scanner on-time 10s
        { 34, 30 }, // Max barcode length 30
        { 35, 1 },  // Store RTC timestamp on
    };

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

    public int GetParam(int paramNumber, byte[] buffer, int length)
    {
        if (!_isConnected) return -1;
        buffer[0] = _mockParams.ContainsKey(paramNumber) ? _mockParams[paramNumber] : (byte)0;
        return 0;
    }

    public int SetParam(int paramNumber, byte[] buffer, int length)
    {
        if (!_isConnected) return -1;
        _mockParams[paramNumber] = buffer[0];
        return 0;
    }

    public int SetDefaults()
    {
        if (!_isConnected) return -1;
        _mockParams[2] = 1;
        _mockParams[4] = 0;
        _mockParams[7] = 3;
        _mockParams[10] = 1;
        _mockParams[11] = 1;
        _mockParams[17] = 10;
        _mockParams[34] = 30;
        _mockParams[35] = 1;
        return 0;
    }
}

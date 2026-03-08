using ABC.Models;

namespace ABC.Services;

public interface IScannerService
{
    List<int> DetectScanners();
    bool Connect(int comPort);
    void Disconnect();
    ScannerInfo GetScannerInfo();
    List<BarcodeEntry> ReadAllBarcodes();
    bool ClearScannerData();
    bool IsConnected { get; }
    int GetParam(int paramNumber, byte[] buffer, int length);
    int SetParam(int paramNumber, byte[] buffer, int length);
    int SetDefaults();
}

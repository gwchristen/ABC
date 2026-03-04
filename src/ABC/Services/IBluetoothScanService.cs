using ABC.Models;

namespace ABC.Services;

public interface IBluetoothScanService
{
    string[] GetAvailableComPorts();
    bool Connect(string portName, int baudRate = 115200);
    void Disconnect();
    bool IsConnected { get; }
    event EventHandler<BarcodeEntry> BarcodeReceived;
}

using ABC.Models;

namespace ABC.Services;

public interface IBluetoothScanService
{
    // Existing SPP methods
    string[] GetAvailableComPorts();
    bool Connect(string portName, int baudRate = 115200);
    void Disconnect();
    bool IsConnected { get; }
    event EventHandler<BarcodeEntry> BarcodeReceived;

    // BLE methods
    Task StartBleDiscoveryAsync();
    void StopBleDiscovery();
    Task<bool> ConnectBleAsync(ulong bluetoothAddress);
    event EventHandler<BleDeviceInfo> BleDeviceDiscovered;
    event EventHandler? Disconnected;
}

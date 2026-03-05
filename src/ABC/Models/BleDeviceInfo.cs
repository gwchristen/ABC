namespace ABC.Models;

public class BleDeviceInfo
{
    public string Name { get; set; } = "";
    public ulong BluetoothAddress { get; set; }
    public string DisplayName => string.IsNullOrEmpty(Name) ? $"BLE Device ({BluetoothAddress:X12})" : Name;
}

using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using ABC.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace ABC.Services;

/// <summary>
/// Real implementation of IBluetoothScanService using System.IO.Ports.SerialPort
/// for Bluetooth SPP (Serial Port Profile) connections, and WinRT BLE APIs
/// for Bluetooth Low Energy connections.
/// </summary>
public class BluetoothScanService : IBluetoothScanService
{
    // Opticon BLE UUIDs (from official OptiConnect SDK)
    private static readonly Guid ScannerServiceUuid = new("46409be5-6967-4557-8e70-784e1e55263b");
    private static readonly Guid OpcServiceUuid = new("6e400000-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid OpcReadCharacteristicUuid = new("6e400002-b5a3-f393-e0a9-e50e24dcca9e");

    // SPP fields
    private SerialPort? _serialPort;

    // BLE fields
    private BluetoothLEAdvertisementWatcher? _bleWatcher;
    private BluetoothLEDevice? _bleDevice;
    private GattCharacteristic? _readCharacteristic;
    private bool _isBleConnected;

    public bool IsConnected => (_serialPort?.IsOpen ?? false) || _isBleConnected;

    public event EventHandler<BarcodeEntry>? BarcodeReceived;
    public event EventHandler<BleDeviceInfo>? BleDeviceDiscovered;
    public event EventHandler? Disconnected;

    public string[] GetAvailableComPorts()
    {
        return SerialPort.GetPortNames();
    }

    public bool Connect(string portName, int baudRate = 115200)
    {
        try
        {
            Disconnect();
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                NewLine = "\r\n",
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();
            return true;
        }
        catch
        {
            _serialPort?.Dispose();
            _serialPort = null;
            return false;
        }
    }

    public void Disconnect()
    {
        // SPP disconnect
        if (_serialPort is not null)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.DataReceived -= OnDataReceived;
                _serialPort.Close();
            }
            _serialPort.Dispose();
            _serialPort = null;
        }

        // BLE disconnect
        DisconnectBle();
    }

    public Task StartBleDiscoveryAsync()
    {
        StopBleDiscovery();

        _bleWatcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        // Filter to Opticon scanners by their advertised Scanner Service UUID
        _bleWatcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(ScannerServiceUuid);

        _bleWatcher.Received += OnBleAdvertisementReceived;
        _bleWatcher.Start();

        Debug.WriteLine("[BLE] Started BLE discovery");
        return Task.CompletedTask;
    }

    public void StopBleDiscovery()
    {
        if (_bleWatcher != null)
        {
            _bleWatcher.Received -= OnBleAdvertisementReceived;
            if (_bleWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                _bleWatcher.Stop();
            _bleWatcher = null;
            Debug.WriteLine("[BLE] Stopped BLE discovery");
        }
    }

    public async Task<bool> ConnectBleAsync(ulong bluetoothAddress)
    {
        try
        {
            DisconnectBle();

            Debug.WriteLine($"[BLE] Connecting to device {bluetoothAddress:X12}...");
            _bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);

            if (_bleDevice == null)
            {
                Debug.WriteLine("[BLE] Failed to get device from address");
                return false;
            }

            var servicesResult = await _bleDevice.GetGattServicesForUuidAsync(OpcServiceUuid);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                Debug.WriteLine($"[BLE] Failed to get OPC service: {servicesResult.Status}");
                DisconnectBle();
                return false;
            }

            var opcService = servicesResult.Services[0];
            var characteristicsResult = await opcService.GetCharacteristicsForUuidAsync(OpcReadCharacteristicUuid);

            if (characteristicsResult.Status != GattCommunicationStatus.Success || characteristicsResult.Characteristics.Count == 0)
            {
                Debug.WriteLine($"[BLE] Failed to get read characteristic: {characteristicsResult.Status}");
                DisconnectBle();
                return false;
            }

            _readCharacteristic = characteristicsResult.Characteristics[0];

            var notifyResult = await _readCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyResult != GattCommunicationStatus.Success)
            {
                Debug.WriteLine($"[BLE] Failed to enable notifications: {notifyResult}");
                DisconnectBle();
                return false;
            }

            _readCharacteristic.ValueChanged += OnBleCharacteristicValueChanged;
            _bleDevice.ConnectionStatusChanged += OnBleConnectionStatusChanged;
            _isBleConnected = true;

            Debug.WriteLine("[BLE] Connected successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BLE] Connection error: {ex.Message}");
            DisconnectBle();
            return false;
        }
    }

    private void DisconnectBle()
    {
        if (_readCharacteristic != null)
        {
            _readCharacteristic.ValueChanged -= OnBleCharacteristicValueChanged;
            _readCharacteristic = null;
        }

        if (_bleDevice != null)
        {
            _bleDevice.ConnectionStatusChanged -= OnBleConnectionStatusChanged;
            _bleDevice.Dispose();
            _bleDevice = null;
        }

        _isBleConnected = false;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_serialPort is null || !_serialPort.IsOpen)
                return;

            string line = _serialPort.ReadLine().Trim();
            if (!string.IsNullOrEmpty(line))
            {
                var entry = new BarcodeEntry
                {
                    Barcode = line,
                    Timestamp = DateTime.Now,
                    CodeType = string.Empty,
                    ScannerId = string.Empty
                };
                BarcodeReceived?.Invoke(this, entry);
            }
        }
        catch (TimeoutException) { }
        catch { }
    }

    private void OnBleAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var deviceInfo = new BleDeviceInfo
        {
            Name = args.Advertisement.LocalName,
            BluetoothAddress = args.BluetoothAddress
        };

        Debug.WriteLine($"[BLE] Discovered: {deviceInfo.DisplayName}");
        BleDeviceDiscovered?.Invoke(this, deviceInfo);
    }

    private void OnBleCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);

            string barcode = Encoding.UTF8.GetString(bytes).Trim();

            if (!string.IsNullOrEmpty(barcode))
            {
                Debug.WriteLine($"[BLE] Received barcode: {barcode}");
                var entry = new BarcodeEntry
                {
                    Barcode = barcode,
                    Timestamp = DateTime.Now,
                    CodeType = string.Empty,
                    ScannerId = string.Empty
                };
                BarcodeReceived?.Invoke(this, entry);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BLE] Error reading characteristic value: {ex.Message}");
        }
    }

    private void OnBleConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        Debug.WriteLine($"[BLE] Connection status changed: {sender.ConnectionStatus}");
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            DisconnectBle();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}

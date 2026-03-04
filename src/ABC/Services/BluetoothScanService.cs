using System.IO.Ports;
using ABC.Models;

namespace ABC.Services;

/// <summary>
/// Real implementation of IBluetoothScanService using System.IO.Ports.SerialPort
/// for Bluetooth SPP (Serial Port Profile) connections.
/// </summary>
public class BluetoothScanService : IBluetoothScanService
{
    private SerialPort? _serialPort;

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    public event EventHandler<BarcodeEntry>? BarcodeReceived;

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
}

using System.IO;
using ABC.Models;

namespace ABC.Services;

/// <summary>
/// Real implementation of IScannerService using the Opticon CSP2 .NET wrapper.
/// Requires Opticon DLLs (Csp2.dll, Csp2Ex.dll, Opticon.csp2.net.dll, Opticon.csp2Ex.net.dll)
/// to be present in the application output directory.
/// Falls back gracefully if DLLs are not found.
/// </summary>
public class OpticonScannerService : IScannerService
{
    private bool _isConnected;
    private int _connectedPort;
    private dynamic? _csp2;

    public bool IsConnected => _isConnected;

    public List<int> DetectScanners()
    {
        try
        {
            var csp2Type = LoadCsp2Type();
            if (csp2Type is null)
                return new List<int>();

            // csp2GetOpnCompatiblePorts returns a comma-separated list of COM port numbers
            dynamic csp2 = Activator.CreateInstance(csp2Type)!;
            var portsResult = csp2.csp2GetOpnCompatiblePorts();
            var ports = new List<int>();
            if (!string.IsNullOrWhiteSpace((string)portsResult))
            {
                foreach (var p in ((string)portsResult).Split(','))
                {
                    if (int.TryParse(p.Trim(), out int port))
                        ports.Add(port);
                }
            }
            return ports;
        }
        catch
        {
            return new List<int>();
        }
    }

    public bool Connect(int comPort)
    {
        try
        {
            var csp2Type = LoadCsp2Type();
            if (csp2Type is null)
                return false;

            _csp2 = Activator.CreateInstance(csp2Type)!;
            int result = _csp2.csp2InitEx(comPort);
            if (result == 0)
            {
                _csp2.csp2WakeUp();
                _connectedPort = comPort;
                _isConnected = true;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Disconnect()
    {
        try
        {
            _csp2?.csp2Restore();
        }
        catch { }
        finally
        {
            _isConnected = false;
            _csp2 = null;
            _connectedPort = 0;
        }
    }

    public ScannerInfo GetScannerInfo()
    {
        if (!_isConnected || _csp2 is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            return new ScannerInfo
            {
                Model = (string)_csp2.csp2GetModel(),
                FirmwareVersion = (string)_csp2.csp2GetFirmwareVersion(),
                SerialNumber = (string)_csp2.csp2GetSerialNumber(),
                BatteryStatus = $"{(int)_csp2.csp2GetBatteryLevel()}%",
                BarcodeCount = (int)_csp2.csp2GetDataCount(),
                ProtocolVersion = (int)_csp2.csp2GetProtocolVersion(),
                ComPort = _connectedPort
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read scanner info: {ex.Message}", ex);
        }
    }

    public List<BarcodeEntry> ReadAllBarcodes()
    {
        if (!_isConnected || _csp2 is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            _csp2.csp2ReadData();
            var barcodes = new List<BarcodeEntry>();
            int seq = 1;
            string barcode;
            while (!string.IsNullOrEmpty(barcode = (string)_csp2.csp2GetPacket()))
            {
                barcodes.Add(new BarcodeEntry
                {
                    Barcode = barcode,
                    Timestamp = DateTime.Now,
                    CodeType = string.Empty,
                    ScannerId = string.Empty,
                    SequenceNumber = seq++
                });
            }
            return barcodes;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read barcodes: {ex.Message}", ex);
        }
    }

    public bool ClearScannerData()
    {
        if (!_isConnected || _csp2 is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            int result = _csp2.csp2ClearData();
            _csp2.csp2Restore();
            return result == 0;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to clear scanner data: {ex.Message}", ex);
        }
    }

    private static Type? LoadCsp2Type()
    {
        try
        {
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Opticon.csp2.net.dll");
            var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
            return assembly.GetType("Opticon.Csp2");
        }
        catch
        {
            return null;
        }
    }
}
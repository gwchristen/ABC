using System.IO;
using System.Text;
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
    private const System.Reflection.BindingFlags AllDeclaredBindingFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.DeclaredOnly;

    private bool _isConnected;
    private int _connectedPort;
    private Type? _csp2Type;
    private Type? _barCodeDataPacketType;

    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets the last error message from detection or connection attempts.
    /// Used for diagnostic purposes when detection silently fails.
    /// </summary>
    public string? LastError { get; private set; }

    public List<int> DetectScanners()
    {
        System.Diagnostics.Debug.WriteLine("[OpticonScannerService] DetectScanners entered");
        LastError = null;
        try
        {
            var csp2Type = LoadCsp2Type();
            if (csp2Type is null)
            {
                // LastError is already set by LoadCsp2Type
                return new List<int>();
            }

            _csp2Type = csp2Type;

            // GetOpnCompatiblePorts takes an Int32[] array and fills it with port numbers
            // It returns the number of ports found (or error code)
            int[] portArray = new int[256]; // generous buffer
            var method = _csp2Type.GetMethod("GetOpnCompatiblePorts", new Type[] { typeof(int[]) });
            var result = (int)(method?.Invoke(null, new object[] { portArray }) ?? -1);
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetOpnCompatiblePorts returned: {result}");

            // result is the count of ports found; extract portArray[0..result-1]
            // If result <= 0, no ports found
            var ports = new List<int>();
            if (result > 0)
            {
                for (int i = 0; i < result; i++)
                    ports.Add(portArray[i]);
            }
            else
            {
                LastError = "GetOpnCompatiblePorts returned no ports. Ensure scanner is connected via USB and powered on.";
            }
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Detected ports: {string.Join(", ", ports.Select(p => $"COM{p}"))}");
            return ports;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] DetectScanners failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            LastError = $"DetectScanners exception: {ex.GetType().Name}: {ex.Message}";
            return new List<int>();
        }
    }

    public bool Connect(int comPort)
    {
        System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Connect to COM{comPort}");
        try
        {
            var csp2Type = LoadCsp2Type();
            if (csp2Type is null)
                return false;

            _csp2Type = csp2Type;

            // Init(int nComPort) - returns 0 on success
            var initMethod = _csp2Type.GetMethod("Init", new Type[] { typeof(int) });
            int initResult = (int)(initMethod?.Invoke(null, new object[] { comPort }) ?? -1);
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Init({comPort}) returned: {initResult}");
            if (initResult == 0)
            {
                // WakeUp() - no-parameter overload
                var wakeMethod = _csp2Type.GetMethod("WakeUp", Type.EmptyTypes);
                var wakeResult = wakeMethod?.Invoke(null, null);
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] WakeUp() returned: {wakeResult}");
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Connected successfully to COM{comPort}");
                _connectedPort = comPort;
                _isConnected = true;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Connect failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    public void Disconnect()
    {
        System.Diagnostics.Debug.WriteLine("[OpticonScannerService] Disconnect");
        try
        {
            // Restore() - no-parameter overload
            var restoreMethod = _csp2Type?.GetMethod("Restore", Type.EmptyTypes);
            restoreMethod?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Disconnect failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
        }
        finally
        {
            _isConnected = false;
            _csp2Type = null;
            _barCodeDataPacketType = null;
            _connectedPort = 0;
        }
    }

    public ScannerInfo GetScannerInfo()
    {
        System.Diagnostics.Debug.WriteLine("[OpticonScannerService] GetScannerInfo entered");
        if (!_isConnected || _csp2Type is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            // GetDeviceId(String& DeviceId) - out parameter
            string deviceId = "";
            var getDeviceIdMethod = _csp2Type.GetMethod("GetDeviceId", new Type[] { typeof(string).MakeByRefType() });
            if (getDeviceIdMethod != null)
            {
                object?[] args = new object?[] { null };
                var idResult = getDeviceIdMethod.Invoke(null, args);
                deviceId = args[0] as string ?? "Unknown";
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetDeviceId returned: {idResult}, deviceId='{deviceId}'");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[OpticonScannerService] GetDeviceId method not found");
            }

            // GetSwVersion(StringBuilder szSwVersion, Int32 nMaxLength)
            string swVersion = "Unknown";
            var getSwVersionMethod = _csp2Type.GetMethod("GetSwVersion", new Type[] { typeof(StringBuilder), typeof(int) });
            if (getSwVersionMethod != null)
            {
                var sb = new StringBuilder(256);
                var swResult = getSwVersionMethod.Invoke(null, new object[] { sb, 256 });
                swVersion = sb.ToString();
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetSwVersion returned: {swResult}, version='{swVersion}'");
            }

            // DataAvailable() - returns barcode count
            int barcodeCount = 0;
            var dataAvailMethod = _csp2Type.GetMethod("DataAvailable", Type.EmptyTypes);
            if (dataAvailMethod != null)
            {
                barcodeCount = (int)dataAvailMethod.Invoke(null, null)!;
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] DataAvailable returned: {barcodeCount}");
            }

            // GetSystemStatus() - returns battery/system info
            int systemStatus = 0;
            var sysStatusMethod = _csp2Type.GetMethod("GetSystemStatus", Type.EmptyTypes);
            if (sysStatusMethod != null)
            {
                systemStatus = (int)sysStatusMethod.Invoke(null, null)!;
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetSystemStatus returned: {systemStatus}");
            }

            // GetProtocol()
            int protocol = 0;
            var protoMethod = _csp2Type.GetMethod("GetProtocol", Type.EmptyTypes);
            if (protoMethod != null)
            {
                protocol = (int)protoMethod.Invoke(null, null)!;
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetProtocol returned: {protocol}");
            }

            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] ScannerInfo: Model='{deviceId}', FW='{swVersion}', Barcodes={barcodeCount}, Status={systemStatus}, Protocol={protocol}");

            return new ScannerInfo
            {
                Model = deviceId,
                FirmwareVersion = swVersion,
                SerialNumber = deviceId, // DeviceId often contains serial
                BatteryStatus = $"Status: {systemStatus}",
                BarcodeCount = barcodeCount,
                ProtocolVersion = protocol,
                ComPort = _connectedPort
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetScannerInfo failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            throw new InvalidOperationException($"Failed to read scanner info: {ex.Message}", ex);
        }
    }

    public List<BarcodeEntry> ReadAllBarcodes()
    {
        System.Diagnostics.Debug.WriteLine("[OpticonScannerService] ReadAllBarcodes entered");
        if (!_isConnected || _csp2Type is null)
        {
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] ReadAllBarcodes precondition failed: IsConnected={_isConnected}, _csp2Type={(_csp2Type != null ? "loaded" : "null")}");
            throw new InvalidOperationException("Scanner is not connected.");
        }

        try
        {
            // ReadData() first - this transfers data from scanner to PC buffer
            var readDataMethod = _csp2Type.GetMethod("ReadData", Type.EmptyTypes);
            if (readDataMethod != null)
            {
                var readResult = readDataMethod.Invoke(null, null);
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] ReadData() returned: {readResult}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[OpticonScannerService] ReadData method not found!");
            }

            // DataAvailable() to get count
            var dataAvailMethod = _csp2Type.GetMethod("DataAvailable", Type.EmptyTypes);
            int count = 0;
            if (dataAvailMethod != null)
            {
                count = (int)dataAvailMethod.Invoke(null, null)!;
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] DataAvailable() returned: {count}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[OpticonScannerService] DataAvailable method not found!");
            }

            if (count <= 0)
            {
                System.Diagnostics.Debug.WriteLine("[OpticonScannerService] No barcodes available (count <= 0), returning empty list");
                return new List<BarcodeEntry>();
            }

            // Get the BarCodeDataPacket type
            var packetType = _barCodeDataPacketType
                ?? _csp2Type.Assembly.GetType("Opticon.csp2+BarCodeDataPacket");
            _barCodeDataPacketType = packetType;
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] BarCodeDataPacket type: {(packetType != null ? packetType.FullName : "NOT FOUND")}, IsValueType={packetType?.IsValueType}");

            // GetPacket(BarCodeDataPacket& aPacket, Int32 nBarcodeNumber) for each barcode
            var getPacketMethod = packetType != null
                ? _csp2Type.GetMethod("GetPacket", new Type[] { packetType.MakeByRefType(), typeof(int) })
                : null;
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetPacket method: {(getPacketMethod != null ? "found" : "NOT FOUND")}");

            var barcodes = new List<BarcodeEntry>();

            // Get the device ID for barcode ScannerId tagging
            string deviceId = "";
            var getDeviceIdMethod = _csp2Type.GetMethod("GetDeviceId", new Type[] { typeof(string).MakeByRefType() });
            if (getDeviceIdMethod != null)
            {
                object?[] idArgs = new object?[] { null };
                getDeviceIdMethod.Invoke(null, idArgs);
                deviceId = idArgs[0] as string ?? "";
            }

            for (int i = 0; i < count; i++)
            {
                if (getPacketMethod == null || packetType == null)
                    break;

                object?[] args = new object?[] { Activator.CreateInstance(packetType), i };
                int result = (int)(getPacketMethod.Invoke(null, args) ?? -1);
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] GetPacket({i}) returned: {result}, packet is {(args[0] != null ? "not null" : "null")}");
                if (result >= 0 && args[0] != null)
                {
                    string barcodeData = "";
                    string codeType = "";
                    DateTime timestamp = DateTime.Now;

                    // Log all fields on first iteration for debugging
                    if (i == 0)
                    {
                        var allFields = packetType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] BarCodeDataPacket fields:");
                        foreach (var f in allFields)
                            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   {f.FieldType.Name} {f.Name}");

                        var allProps = packetType.GetProperties();
                        System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] BarCodeDataPacket properties:");
                        foreach (var p in allProps)
                            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   {p.PropertyType.Name} {p.Name}");
                    }

                    try
                    {
                        var barcodeField = packetType.GetField("BarData") ?? packetType.GetField("barData") ?? packetType.GetField("Data");
                        var codeTypeField = packetType.GetField("CodeId") ?? packetType.GetField("codeId") ?? packetType.GetField("CodeType");
                        var timeField = packetType.GetField("TimeStamp") ?? packetType.GetField("timeStamp");

                        if (barcodeField != null)
                        {
                            var val = barcodeField.GetValue(args[0]);
                            barcodeData = val?.ToString() ?? "";
                        }

                        if (codeTypeField != null)
                        {
                            var val = codeTypeField.GetValue(args[0]);
                            codeType = val?.ToString() ?? "";
                        }

                        if (timeField != null)
                        {
                            var val = timeField.GetValue(args[0]);
                            if (val is DateTime dt)
                                timestamp = dt;
                        }

                        System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Barcode[{i}]: data='{barcodeData}', codeType='{codeType}', timestamp={timestamp}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Error reading packet fields: {ex.Message}");
                        // Fallback: try ToString
                        barcodeData = args[0]?.ToString() ?? $"Barcode_{i + 1}";
                    }

                    barcodes.Add(new BarcodeEntry
                    {
                        Barcode = barcodeData,
                        Timestamp = timestamp,
                        CodeType = codeType,
                        ScannerId = deviceId ?? "",
                        SequenceNumber = i + 1
                    });
                }
            }
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] ReadAllBarcodes returning {barcodes.Count} barcode(s)");
            return barcodes;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read barcodes: {ex.Message}", ex);
        }
    }

    public bool ClearScannerData()
    {
        if (!_isConnected || _csp2Type is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            // ClearData() - returns 0 on success
            var clearMethod = _csp2Type.GetMethod("ClearData", Type.EmptyTypes);
            int result = clearMethod != null ? (int)(clearMethod.Invoke(null, null) ?? -1) : -1;
            // Also call Restore()
            var restoreMethod = _csp2Type.GetMethod("Restore", Type.EmptyTypes);
            restoreMethod?.Invoke(null, null);
            return result == 0;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to clear scanner data: {ex.Message}", ex);
        }
    }

    private object? InvokeStatic(string methodName, params object[] args)
    {
        var method = _csp2Type?.GetMethod(methodName);
        if (method == null)
        {
            // Try all binding flags to find the method regardless of visibility or instance/static
            method = _csp2Type?.GetMethod(methodName, AllDeclaredBindingFlags & ~System.Reflection.BindingFlags.DeclaredOnly);
            if (method != null)
            {
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Method '{methodName}' found with extended binding flags (IsStatic={method.IsStatic}, IsPublic={method.IsPublic})");
                if (!method.IsStatic)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Method '{methodName}' is an instance method and cannot be invoked without an instance; skipping");
                    return null;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] Method '{methodName}' not found on type '{_csp2Type?.FullName}'");
                return null;
            }
        }
        var result = method.Invoke(null, args.Length > 0 ? args : Array.Empty<object>());
        System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] {methodName} returned: {result} (type: {result?.GetType().Name ?? "null"})");
        return result;
    }

    private Type? LoadCsp2Type()
    {
        try
        {
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Opticon.csp2.net.dll");
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type: trying DLL path: {dllPath}");
            if (!File.Exists(dllPath))
            {
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type: DLL not found at {dllPath}");
                LastError = $"Opticon DLL not found at: {dllPath}";
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type: DLL found, loading assembly");
            var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
            var csp2Type = assembly.GetType("Opticon.csp2");
            if (csp2Type is null)
            {
                var availableTypes = string.Join(", ", assembly.GetExportedTypes().Select(t => t.FullName));
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type: type 'Opticon.csp2' not found. Available: {availableTypes}");
                LastError = $"Type 'Opticon.csp2' not found in assembly. Available types: {availableTypes}";
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type: type 'Opticon.csp2' loaded successfully");
                var allMethods = csp2Type.GetMethods(AllDeclaredBindingFlags);
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type: found {allMethods.Length} declared methods:");
                foreach (var m in allMethods)
                {
                    var parameters = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({parameters})");
                }
            }
            return csp2Type;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService] LoadCsp2Type failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            System.Diagnostics.Debug.WriteLine($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            LastError = $"LoadCsp2Type failed: {ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
                LastError += $" Inner: {ex.InnerException.Message}";
            return null;
        }
    }
}
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using ABC.Models;

namespace ABC.Services;

/// <summary>
/// Real implementation of IScannerService using the Opticon CSP2 .NET wrapper.
/// Requires Opticon DLLs (Csp2Ex.dll, Opticon.csp2Ex.net.dll)
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
        LogService.Debug("[OpticonScannerService] DetectScanners entered");
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
            LogService.Debug($"[OpticonScannerService] GetOpnCompatiblePorts returned: {result}");

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
            LogService.Debug($"[OpticonScannerService] Detected ports: {string.Join(", ", ports.Select(p => $"COM{p}"))}");
            return ports;
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] DetectScanners failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            LogService.Error($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            LastError = $"DetectScanners exception: {ex.GetType().Name}: {ex.Message}";
            return new List<int>();
        }
    }

    public bool Connect(int comPort)
    {
        LogService.Debug($"[OpticonScannerService] Connect to COM{comPort}");
        try
        {
            var csp2Type = LoadCsp2Type();
            if (csp2Type is null)
                return false;

            _csp2Type = csp2Type;

            // Init(int nComPort) - returns 0 on success
            var initMethod = _csp2Type.GetMethod("Init", new Type[] { typeof(int) });
            int initResult = (int)(initMethod?.Invoke(null, new object[] { comPort }) ?? -1);
            LogService.Debug($"[OpticonScannerService] Init({comPort}) returned: {initResult}");
            if (initResult == 0)
            {
                // WakeUp() - no-parameter overload
                var wakeMethod = _csp2Type.GetMethod("WakeUp", Type.EmptyTypes);
                var wakeResult = wakeMethod?.Invoke(null, null);
                LogService.Debug($"[OpticonScannerService] WakeUp() returned: {wakeResult}");
                LogService.Debug($"[OpticonScannerService] Connected successfully to COM{comPort}");
                _connectedPort = comPort;
                _isConnected = true;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] Connect failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            LogService.Error($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    [HandleProcessCorruptedStateExceptions]
    [SecurityCritical]
    public void Disconnect()
    {
        LogService.Debug("[OpticonScannerService] Disconnect");
        try
        {
            if (_isConnected && _csp2Type != null)
            {
                var restoreMethod = _csp2Type.GetMethod("Restore", Type.EmptyTypes);
                restoreMethod?.Invoke(null, null);
            }
        }
        catch (AccessViolationException ave)
        {
            LogService.Error($"[OpticonScannerService] Disconnect encountered AccessViolationException (native DLL memory error): {ave.Message}");
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] Disconnect failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            LogService.Error($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
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
        LogService.Debug("[OpticonScannerService] GetScannerInfo entered");
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
                LogService.Debug($"[OpticonScannerService] GetDeviceId returned: {idResult}, deviceId='{deviceId}'");
            }
            else
            {
                LogService.Error("[OpticonScannerService] GetDeviceId method not found");
            }

            // GetSwVersion(StringBuilder szSwVersion, Int32 nMaxLength)
            string swVersion = "Unknown";
            var getSwVersionMethod = _csp2Type.GetMethod("GetSwVersion", new Type[] { typeof(StringBuilder), typeof(int) });
            if (getSwVersionMethod != null)
            {
                var sb = new StringBuilder(256);
                var swResult = getSwVersionMethod.Invoke(null, new object[] { sb, 256 });
                swVersion = sb.ToString();
                LogService.Debug($"[OpticonScannerService] GetSwVersion returned: {swResult}, version='{swVersion}'");
            }

            // DataAvailable() - returns barcode count
            int barcodeCount = 0;
            var dataAvailMethod = _csp2Type.GetMethod("DataAvailable", Type.EmptyTypes);
            if (dataAvailMethod != null)
            {
                barcodeCount = (int)dataAvailMethod.Invoke(null, null)!;
                LogService.Debug($"[OpticonScannerService] DataAvailable returned: {barcodeCount}");
            }

            // GetSystemStatus() - returns battery/system info
            int systemStatus = 0;
            var sysStatusMethod = _csp2Type.GetMethod("GetSystemStatus", Type.EmptyTypes);
            if (sysStatusMethod != null)
            {
                systemStatus = (int)sysStatusMethod.Invoke(null, null)!;
                LogService.Debug($"[OpticonScannerService] GetSystemStatus returned: {systemStatus}");
            }

            // GetProtocol()
            int protocol = 0;
            var protoMethod = _csp2Type.GetMethod("GetProtocol", Type.EmptyTypes);
            if (protoMethod != null)
            {
                protocol = (int)protoMethod.Invoke(null, null)!;
                LogService.Debug($"[OpticonScannerService] GetProtocol returned: {protocol}");
            }

            LogService.Debug($"[OpticonScannerService] ScannerInfo: Model='{deviceId}', FW='{swVersion}', Barcodes={barcodeCount}, Status={systemStatus}, Protocol={protocol}");

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
            LogService.Error($"[OpticonScannerService] GetScannerInfo failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            LogService.Error($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            throw new InvalidOperationException($"Failed to read scanner info: {ex.Message}", ex);
        }
    }

    public List<BarcodeEntry> ReadAllBarcodes()
    {
        LogService.Debug("[OpticonScannerService] ReadAllBarcodes entered");
        if (!_isConnected || _csp2Type is null)
        {
            LogService.Error($"[OpticonScannerService] ReadAllBarcodes precondition failed: IsConnected={_isConnected}, _csp2Type={(_csp2Type != null ? "loaded" : "null")}");
            throw new InvalidOperationException("Scanner is not connected.");
        }

        try
        {
            // ReadData() first - this transfers data from scanner to PC buffer and returns the count
            var readDataMethod = _csp2Type.GetMethod("ReadData", Type.EmptyTypes);
            int count = 0;
            if (readDataMethod != null)
            {
                var readResult = readDataMethod.Invoke(null, null);
                LogService.Debug($"[OpticonScannerService] ReadData() returned: {readResult}");
                if (readResult is int readCount && readCount > 0)
                {
                    count = readCount;
                }
            }
            else
            {
                LogService.Error("[OpticonScannerService] ReadData method not found!");
            }

            // Fall back to DataAvailable() if ReadData() returned 0 or a negative value
            if (count <= 0)
            {
                var dataAvailMethod = _csp2Type.GetMethod("DataAvailable", Type.EmptyTypes);
                if (dataAvailMethod != null)
                {
                    var dataAvailResult = (int)dataAvailMethod.Invoke(null, null)!;
                    LogService.Debug($"[OpticonScannerService] DataAvailable() returned: {dataAvailResult} (fallback)");
                    count = dataAvailResult;
                }
                else
                {
                    LogService.Error("[OpticonScannerService] DataAvailable method not found!");
                }
            }

            if (count <= 0)
            {
                LogService.Debug("[OpticonScannerService] No barcodes available (count <= 0), returning empty list");
                return new List<BarcodeEntry>();
            }

            // Get the BarCodeDataPacket type
            var packetType = _barCodeDataPacketType
                ?? _csp2Type.Assembly.GetType("Opticon.csp2+BarCodeDataPacket");
            _barCodeDataPacketType = packetType;
            LogService.Debug($"[OpticonScannerService] BarCodeDataPacket type: {(packetType != null ? packetType.FullName : "NOT FOUND")}, IsValueType={packetType?.IsValueType}");

            // GetPacket(BarCodeDataPacket& aPacket, Int32 nBarcodeNumber) for each barcode
            var getPacketMethod = packetType != null
                ? _csp2Type.GetMethod("GetPacket", new Type[] { packetType.MakeByRefType(), typeof(int) })
                : null;
            LogService.Debug($"[OpticonScannerService] GetPacket method: {(getPacketMethod != null ? "found" : "NOT FOUND")}");

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
                LogService.Debug($"[OpticonScannerService] GetPacket({i}) returned: {result}, packet is {(args[0] != null ? "not null" : "null")}");
                if (result >= 0 && args[0] != null)
                {
                    string barcodeData = "";
                    string codeType = "";
                    DateTime timestamp = DateTime.Now;

                    // Log all fields on first iteration for debugging
                    if (i == 0)
                    {
                        var allFields = packetType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        LogService.Debug($"[OpticonScannerService] BarCodeDataPacket fields:");
                        foreach (var f in allFields)
                            LogService.Debug($"[OpticonScannerService]   {f.FieldType.Name} {f.Name}");

                        var allProps = packetType.GetProperties();
                        LogService.Debug($"[OpticonScannerService] BarCodeDataPacket properties:");
                        foreach (var p in allProps)
                            LogService.Debug($"[OpticonScannerService]   {p.PropertyType.Name} {p.Name}");
                    }

                    try
                    {
                        var barcodeField = packetType.GetField("strBarData");
                        var codeTypeField = packetType.GetField("strId");
                        var codeIdField = packetType.GetField("iId");
                        var timeField = packetType.GetField("dtTimestamp");

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
                        if (string.IsNullOrEmpty(codeType) && codeIdField != null)
                        {
                            var val = codeIdField.GetValue(args[0]);
                            codeType = val?.ToString() ?? "";
                        }

                        if (timeField != null)
                        {
                            var val = timeField.GetValue(args[0]);
                            if (val is DateTime dt)
                                timestamp = dt;
                        }

                        LogService.Debug($"[OpticonScannerService] Barcode[{i}]: data='{barcodeData}', codeType='{codeType}', timestamp={timestamp}");
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"[OpticonScannerService] Error reading packet fields: {ex.Message}");
                        // Fallback: try ToString
                        barcodeData = args[0]?.ToString() ?? $"Barcode_{i}";
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
            LogService.Debug($"[OpticonScannerService] ReadAllBarcodes returning {barcodes.Count} barcode(s)");
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

    public int GetParam(int paramNumber, byte[] buffer, int length)
    {
        if (!_isConnected || _csp2Type is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            var method = _csp2Type.GetMethod("GetParam", new Type[] { typeof(int), typeof(byte[]), typeof(int) });
            if (method == null)
            {
                LogService.Error("[OpticonScannerService] GetParam method not found");
                return -1;
            }
            var result = (int)(method.Invoke(null, new object[] { paramNumber, buffer, length }) ?? -1);
            LogService.Debug($"[OpticonScannerService] GetParam({paramNumber}) returned: {result}, buffer[0]={(buffer.Length > 0 ? buffer[0] : (byte)0)}");
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] GetParam failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return -1;
        }
    }

    public int SetParam(int paramNumber, byte[] buffer, int length)
    {
        if (!_isConnected || _csp2Type is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            var method = _csp2Type.GetMethod("SetParam", new Type[] { typeof(int), typeof(byte[]), typeof(int) });
            if (method == null)
            {
                LogService.Error("[OpticonScannerService] SetParam method not found");
                return -1;
            }
            var result = (int)(method.Invoke(null, new object[] { paramNumber, buffer, length }) ?? -1);
            LogService.Debug($"[OpticonScannerService] SetParam({paramNumber}, {(buffer.Length > 0 ? buffer[0] : (byte)0)}) returned: {result}");
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] SetParam failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return -1;
        }
    }

    public int SetDefaults()
    {
        if (!_isConnected || _csp2Type is null)
            throw new InvalidOperationException("Scanner is not connected.");

        try
        {
            var method = _csp2Type.GetMethod("SetDefaults", Type.EmptyTypes);
            if (method == null)
            {
                LogService.Error("[OpticonScannerService] SetDefaults method not found");
                return -1;
            }
            var result = (int)(method.Invoke(null, null) ?? -1);
            LogService.Debug($"[OpticonScannerService] SetDefaults returned: {result}");
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] SetDefaults failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return -1;
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
                LogService.Debug($"[OpticonScannerService] Method '{methodName}' found with extended binding flags (IsStatic={method.IsStatic}, IsPublic={method.IsPublic})");
                if (!method.IsStatic)
                {
                    LogService.Debug($"[OpticonScannerService] Method '{methodName}' is an instance method and cannot be invoked without an instance; skipping");
                    return null;
                }
            }
            else
            {
                LogService.Error($"[OpticonScannerService] Method '{methodName}' not found on type '{_csp2Type?.FullName}'");
                return null;
            }
        }
        var result = method.Invoke(null, args.Length > 0 ? args : Array.Empty<object>());
        LogService.Debug($"[OpticonScannerService] {methodName} returned: {result} (type: {result?.GetType().Name ?? "null"})");
        return result;
    }

    private Type? LoadCsp2Type()
    {
        try
        {
            // For single-file publish, extracted DLLs go to AppContext.BaseDirectory.
            // For normal builds, DLLs are next to the exe.
            string baseDir = AppContext.BaseDirectory;
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? baseDir) ?? baseDir;

            string dllPath = Path.Combine(baseDir, "Opticon.csp2Ex.net.dll");
            if (!File.Exists(dllPath))
                dllPath = Path.Combine(exeDir, "Opticon.csp2Ex.net.dll");
            LogService.Debug($"[OpticonScannerService] LoadCsp2Type: trying DLL path: {dllPath}");
            if (!File.Exists(dllPath))
            {
                LogService.Error($"[OpticonScannerService] LoadCsp2Type: DLL not found at {dllPath}");
                LastError = $"Opticon DLL not found at: {dllPath}";
                return null;
            }

            LogService.Debug($"[OpticonScannerService] LoadCsp2Type: DLL found, loading assembly");
            var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
            var csp2Type = assembly.GetType("Opticon.csp2");
            if (csp2Type is null)
            {
                var availableTypes = string.Join(", ", assembly.GetExportedTypes().Select(t => t.FullName));
                LogService.Error($"[OpticonScannerService] LoadCsp2Type: type 'Opticon.csp2' not found. Available: {availableTypes}");
                LastError = $"Type 'Opticon.csp2' not found in assembly. Available types: {availableTypes}";
            }
            else
            {
                LogService.Debug($"[OpticonScannerService] LoadCsp2Type: type 'Opticon.csp2' loaded successfully");
                var allMethods = csp2Type.GetMethods(AllDeclaredBindingFlags);
                LogService.Debug($"[OpticonScannerService] LoadCsp2Type: found {allMethods.Length} declared methods:");
                foreach (var m in allMethods)
                {
                    var parameters = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    LogService.Debug($"[OpticonScannerService]   {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({parameters})");
                }
            }
            return csp2Type;
        }
        catch (Exception ex)
        {
            LogService.Error($"[OpticonScannerService] LoadCsp2Type failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogService.Error($"[OpticonScannerService]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            LogService.Error($"[OpticonScannerService]   StackTrace: {ex.StackTrace}");
            LastError = $"LoadCsp2Type failed: {ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
                LastError += $" Inner: {ex.InnerException.Message}";
            return null;
        }
    }
}
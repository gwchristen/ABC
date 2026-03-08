using System.IO;
using Serilog;

namespace ABC.Services;

/// <summary>
/// Static logging helper using Serilog. Logs to a rolling daily file in %LOCALAPPDATA%/ABC/logs/.
/// </summary>
public static class LogService
{
    private static readonly Lazy<ILogger> _logger = new(CreateLogger);

    private static ILogger Logger => _logger.Value;

    private static ILogger CreateLogger()
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABC", "logs");
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "abc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    public static void Debug(string message) => Logger.Debug(message);
    public static void Debug(string messageTemplate, params object?[] values) => Logger.Debug(messageTemplate, values);

    public static void Info(string message) => Logger.Information(message);
    public static void Info(string messageTemplate, params object?[] values) => Logger.Information(messageTemplate, values);

    public static void Warning(string message) => Logger.Warning(message);
    public static void Warning(string messageTemplate, params object?[] values) => Logger.Warning(messageTemplate, values);

    public static void Error(string message) => Logger.Error(message);
    public static void Error(string messageTemplate, params object?[] values) => Logger.Error(messageTemplate, values);
    public static void Error(Exception ex, string message) => Logger.Error(ex, message);
    public static void Error(Exception ex, string messageTemplate, params object?[] values) => Logger.Error(ex, messageTemplate, values);
}

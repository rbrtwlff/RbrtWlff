using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AkteTimer.Services;

public static class LogService
{
    private static readonly object SyncRoot = new();
    private static string? _logFilePath;

    public static void Initialize(string logDirectory)
    {
        if (_logFilePath != null)
        {
            return;
        }

        SetLogFilePath(logDirectory);
        LogInfo("Logging initialisiert.");
    }

    public static void UpdateLogDirectory(string logDirectory)
    {
        SetLogFilePath(logDirectory);
        LogInfo("Logging-Pfad aktualisiert.");
    }

    public static void LogInfo(string message)
    {
        Write("INFO", message);
    }

    public static void LogError(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    public static void LogException(Exception exception, string context)
    {
        LogError($"{context}: {exception.Message}", exception);
    }

    private static void Write(string level, string message, Exception? exception = null)
    {
        if (_logFilePath == null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(level);
        builder.Append("] ");
        builder.AppendLine(message);

        if (exception != null)
        {
            builder.AppendLine(exception.ToString());
        }

        lock (SyncRoot)
        {
            File.AppendAllText(_logFilePath, builder.ToString());
        }
    }

    private static void SetLogFilePath(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var fileName = $"AkteTimer-{DateTime.UtcNow:yyyyMMdd}.log";
        _logFilePath = Path.Combine(logDirectory, fileName);
    }
}

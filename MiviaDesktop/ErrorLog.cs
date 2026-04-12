namespace MiviaDesktop;

using System;
using System.IO;

public enum LogLevel
{
    Error = 0,
    Info = 1,
    Debug = 2
}

public sealed class ErrorLogger
{
    private static ErrorLogger _instance;
    private static readonly object LockObject = new object();

    private readonly string _logFilePath;

    /// <summary>
    /// Controls which log messages are written. Default: Error only.
    /// Set to Info or Debug for more verbose logging.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Error;

    // Private constructor to prevent direct instantiation
    private ErrorLogger()
    {
        string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string logsDirectory = Path.Combine(executableDirectory, "log");
        Directory.CreateDirectory(logsDirectory);

        string logFileName = $"log_{DateTime.Now:yyyyMMddHHmmss}.json";
        _logFilePath = Path.Combine(logsDirectory, logFileName);
    }

    // Singleton instance property
    public static ErrorLogger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    if (_instance == null)
                    {
                        _instance = new ErrorLogger();
                    }
                }
            }

            return _instance;
        }
    }

    /// <summary>
    /// Log an error message (always logged regardless of level).
    /// </summary>
    public void LogError(string errorMessage)
    {
        WriteEntry("ERROR", errorMessage);
    }

    /// <summary>
    /// Log an informational message (logged when Level >= Info).
    /// </summary>
    public void LogInfo(string message)
    {
        if (Level < LogLevel.Info) return;
        WriteEntry("INFO", message);
    }

    /// <summary>
    /// Log a debug message (logged only when Level == Debug).
    /// </summary>
    public void LogDebug(string message)
    {
        if (Level < LogLevel.Debug) return;
        WriteEntry("DEBUG", message);
    }

    private void WriteEntry(string level, string message)
    {
        var logEntry = new ErrorLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        string jsonData = System.Text.Json.JsonSerializer.Serialize(logEntry);

        try
        {
            using (StreamWriter writer = File.AppendText(_logFilePath))
            {
                writer.WriteLine(jsonData);
            }
        }
        catch (Exception)
        {
            // Logs cannot be written to the log file
        }
    }
}

public class ErrorLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

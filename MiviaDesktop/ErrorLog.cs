namespace MiviaDesktop;

using System;
using System.IO;

public sealed class ErrorLogger
{
    private static ErrorLogger _instance;
    private static readonly object LockObject = new object();

    private readonly string _logFilePath;

    // Private constructor to prevent direct instantiation
    private ErrorLogger()
    {
        string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string logsDirectory = Path.Combine(executableDirectory, "log");
        Directory.CreateDirectory(logsDirectory); // Create the "log" directory if it doesn't exist

        string logFileName = $"error_log_{DateTime.Now:yyyyMMddHHmmss}.json";
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

    // Log an error
    public void LogError(string errorMessage)
    {
        ErrorLogEntry logEntry = new ErrorLogEntry
        {
            Timestamp = DateTime.Now,
            Message = errorMessage
        };

        string jsonData = System.Text.Json.JsonSerializer.Serialize(logEntry);

        // Append the log entry to the log file
        using (StreamWriter writer = File.AppendText(_logFilePath))
        {
            writer.WriteLine(jsonData);
        }
    }
}

public class ErrorLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; }
}
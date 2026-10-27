using System.Globalization;

namespace SectorRemovalUpdater.Gui.ViewModels;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class LogEntry
{
    public LogEntry(DateTime timestamp, LogLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public LogLevel Level { get; }

    public string Message { get; }

    public string Display => string.Create(
        CultureInfo.InvariantCulture,
        $"[{Timestamp:HH:mm:ss}] {Message}");

    public bool IsError => Level == LogLevel.Error;

    public bool IsWarning => Level == LogLevel.Warning;

    public bool IsSuccess => Level == LogLevel.Success;
}

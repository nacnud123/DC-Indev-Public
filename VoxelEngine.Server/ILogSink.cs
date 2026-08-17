using System.Collections.Concurrent;

namespace VoxelEngine.Server;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Chat,
    Command
}

public interface ILogSink
{
    void Log(LogLevel level, string message);
}

public sealed class ConsoleLogSink : ILogSink
{
    public void Log(LogLevel level, string message)
    {
        var prefix = DateTime.Now.ToString("HH:mm:ss");
        Console.WriteLine($"[{prefix}] [{level}] {message}");
    }
}

public sealed class QueuedLogSink : ILogSink
{
    public readonly ConcurrentQueue<(DateTime time, LogLevel level, string message)> Pending = new();
    public void Log(LogLevel level, string message) => Pending.Enqueue((DateTime.Now, level, message));
}
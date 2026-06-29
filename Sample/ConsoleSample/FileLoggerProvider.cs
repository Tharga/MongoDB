using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ConsoleSample;

/// <summary>
/// Minimal, dependency-free file logger for local diagnostics. Truncates on startup so each run is a
/// fresh file. Sample-only; wired up under Development.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _lock = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        try { File.WriteAllText(_path, $"=== Log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"); }
        catch { /* best effort */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose() { }

    private void Write(string text)
    {
        lock (_lock)
        {
            try { File.AppendAllText(_path, text); } catch { /* best effort */ }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string category, FileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append(" [").Append(logLevel).Append("] ")
                .Append(_category).Append(": ")
                .Append(formatter(state, exception))
                .Append(Environment.NewLine);
            if (exception != null) sb.Append(exception).Append(Environment.NewLine);

            _provider.Write(sb.ToString());
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

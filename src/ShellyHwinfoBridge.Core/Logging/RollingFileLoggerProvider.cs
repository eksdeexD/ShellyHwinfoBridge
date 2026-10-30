using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ShellyHwinfoBridge.Core.Logging;

/// <summary>
/// A minimal rolling text-file logger: writes to <c>&lt;name&gt;.log</c> and, once it would exceed the size cap,
/// shifts it to <c>&lt;name&gt;.1.log</c>, <c>&lt;name&gt;.2.log</c> … keeping at most <c>maxFiles</c> files.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    /// <summary>Default size cap per file (about 1 MB).</summary>
    public const long DefaultMaxFileBytes = 1024 * 1024;

    /// <summary>Default number of files kept.</summary>
    public const int DefaultMaxFiles = 3;

    private readonly object _lock = new();
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly LogLevel _minLevel;

    /// <summary>Creates a provider.</summary>
    /// <param name="directory">The log directory (created if missing).</param>
    /// <param name="baseName">File base name.</param>
    /// <param name="maxFileBytes">Size cap per file.</param>
    /// <param name="maxFiles">Number of files kept, including the current one.</param>
    /// <param name="minLevel">Minimum level written.</param>
    public RollingFileLoggerProvider(
        string directory,
        string baseName = AppPaths.AppName,
        long maxFileBytes = DefaultMaxFileBytes,
        int maxFiles = DefaultMaxFiles,
        LogLevel minLevel = LogLevel.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        _directory = directory;
        _baseName = baseName;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
        _minLevel = minLevel;
    }

    /// <summary>Gets the path of the current log file.</summary>
    public string CurrentFilePath => FilePath(0);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minLevel;

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var sb = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelText(level)).Append("] ")
            .Append(category).Append(": ").Append(message);
        if (exception is not null)
        {
            sb.Append(Environment.NewLine).Append(exception);
        }

        sb.Append(Environment.NewLine);
        var text = sb.ToString();

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var current = new FileInfo(CurrentFilePath);
                if (current.Exists && current.Length + Encoding.UTF8.GetByteCount(text) > _maxFileBytes)
                {
                    Roll();
                }

                using var stream = new FileStream(CurrentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never crash the app.
            }
        }
    }

    private string FilePath(int index) =>
        Path.Combine(_directory, index == 0 ? $"{_baseName}.log" : $"{_baseName}.{index}.log");

    private void Roll()
    {
        File.Delete(FilePath(_maxFiles - 1));
        for (var i = _maxFiles - 2; i >= 0; i--)
        {
            if (File.Exists(FilePath(i)))
            {
                File.Move(FilePath(i), FilePath(i + 1), overwrite: true);
            }
        }
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class RollingFileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}

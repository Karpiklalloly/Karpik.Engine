namespace Karpik.Engine.Shared;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public interface ILogger : IDisposable
{
    void Log(string message, LogLevel level = LogLevel.Debug);
    void Log(string source, string message, LogLevel level = LogLevel.Debug);
    void SetMinLevel(LogLevel level);
}

public abstract class Logger(ILogger logger)
{
    public static ILogger Instance { get; } = new ConsoleLogger();
}

public abstract class LoggerDecorator : ILogger
{
    protected readonly ILogger _logger;
    protected LogLevel _minLevel = LogLevel.Debug;
    
    private bool _isDisposed;

    protected LoggerDecorator(ILogger logger)
    {
        _logger = logger;
    }

    public virtual void Log(string message, LogLevel level = LogLevel.Debug)
    {
        _logger?.Log(message, level);
    }

    public void Log(string source, string message, LogLevel level = LogLevel.Debug) => Log($"{source} + {message}", level);

    public virtual void SetMinLevel(LogLevel level)
    {
        _minLevel = level;
        _logger?.SetMinLevel(level);
    }

    protected string GetMessage(LogLevel level, string message)
    {
        return $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }
        
        _isDisposed = true;
        if (disposing)
        {
            _logger?.Dispose();
        }
    }

    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    ~LoggerDecorator()
    {
        Dispose(true);
    }
}

public sealed class ConsoleLogger : LoggerDecorator
{
    private readonly SemaphoreSlim _consoleLock = new SemaphoreSlim(1, 1);

    public ConsoleLogger(ILogger logger = null) : base(logger) { }

    public override async void Log(string message, LogLevel level = LogLevel.Debug)
    {
        if (level < _minLevel) return;

        string formatted = GetMessage(level, message);
        
        await _consoleLock.WaitAsync();
        try
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(level);
            Console.WriteLine(formatted);
            Console.ForegroundColor = originalColor;
        }
        finally
        {
            _consoleLock.Release();
        }

        base.Log(message, level);
    }

    private ConsoleColor GetColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };
    }
}

public sealed class FileLogger : LoggerDecorator
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private StreamWriter _writer;
    private bool _disposed;

    public FileLogger(string filePath, ILogger logger = null) : base(logger)
    {
        _filePath = filePath;
        Initialize();
    }

    private void Initialize()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        _writer = new StreamWriter(_filePath, true)
        {
            AutoFlush = true
        };
    }

    public override async void Log(string message, LogLevel level = LogLevel.Debug)
    {
        if (level < _minLevel) return;

        string formatted = FormatMessage(level, message);
        
        await _fileLock.WaitAsync();
        try
        {
            if (_writer != null)
            {
                await _writer.WriteLineAsync(formatted);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"File log error: {e.Message}");
        }
        finally
        {
            _fileLock.Release();
        }

        base.Log(message, level);
    }

    private string FormatMessage(LogLevel level, string message)
    {
        return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            _fileLock.Wait();
            try
            {
                _writer?.Close();
                _writer?.Dispose();
                _writer = null;
                _fileLock.Dispose();
            }
            finally
            {
                _fileLock.Release();
            }
        }
        
        base.Dispose(disposing);
        _disposed = true;
    }
}
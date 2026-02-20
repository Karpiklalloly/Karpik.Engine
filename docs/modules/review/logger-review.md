# Logger - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Singleton нельзя заменить
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:21-24)

```csharp
public abstract class Logger(ILogger logger)
{
    public static ILogger Instance { get; } = new ConsoleLogger();
}
```

**Проблема**: `Instance` создаётся статически и не может быть заменён. Даже если `LoggerInstaller` регистрирует другой логгер, `Logger.Instance` всегда возвращает `ConsoleLogger`.

**Решение**:
```csharp
public abstract class Logger
{
    private static ILogger? _instance;
    
    public static ILogger Instance
    {
        get => _instance ??= new ConsoleLogger();
        set => _instance = value;
    }
    
    internal static void SetInstance(ILogger logger)
    {
        _instance = logger;
    }
}
```

---

### CR-002: Dispose в финализаторе вызывает Dispose(true)
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:77-80)

```csharp
~LoggerDecorator()
{
    Dispose(true);
}
```

**Проблема**: Финализатор должен вызывать `Dispose(false)`, а не `Dispose(true)`. Вызов `Dispose(true)` в финализаторе может привести к обращению к уже освобождённым управляемым ресурсам.

**Решение**:
```csharp
~LoggerDecorator()
{
    Dispose(false);
}
```

---

## 🟡 Высокий приоритет

### HI-001: Асинхронная запись в консоль
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:89-107)

```csharp
public override async JobHandle Log(string message, LogLevel level = LogLevel.Debug)
{
    // ...
    await _consoleLock.WaitAsync();
    // ...
    Console.WriteLine(formatted);
    // ...
}
```

**Проблема**: Асинхронная запись в консоль с блокировкой создаёт overhead. `Console.WriteLine` уже синхронизирован внутри.

**Решение**: Использовать `Channel<T>` для очереди сообщений:
```csharp
private readonly Channel<string> _messageChannel = Channel.CreateUnbounded<string>();

public ConsoleLogger()
{
    _ = ProcessMessagesAsync();
}

private async Task ProcessMessagesAsync()
{
    await foreach (var message in _messageChannel.Reader.ReadAllAsync())
    {
        Console.WriteLine(message);
    }
}

public override JobHandle Log(string message, LogLevel level = LogLevel.Debug)
{
    if (level < _minLevel) return JobHandle.Completed;
    
    var formatted = GetMessage(level, message);
    _messageChannel.Writer.TryWrite(formatted);
    
    return base.Log(message, level);
}
```

---

### HI-002: FileLogger не обрабатывает ошибки при Initialize
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:136-148)

```csharp
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
```

**Проблема**: Если файл заблокирован или нет прав доступа, исключение не обрабатывается.

**Решение**:
```csharp
private void Initialize()
{
    try
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
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to initialize file logger: {ex.Message}");
        _writer = null;
    }
}
```

---

### HI-003: Race condition в FileLogger.Dispose
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:181-203)

```csharp
protected override void Dispose(bool disposing)
{
    if (_disposed) return;
    
    if (disposing)
    {
        _fileLock.Wait();  // Может зависнуть если Log в процессе
        // ...
    }
    // ...
    _disposed = true;
}
```

**Проблема**: `_disposed` устанавливается после `base.Dispose()`, что может привести к двойному освобождению.

**Решение**:
```csharp
protected override void Dispose(bool disposing)
{
    if (_disposed) return;
    _disposed = true;  // Установить флаг сразу
    
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
}
```

---

### HI-004: LoggerInstaller не создаёт FileLogger
**Файл**: [`LoggerInstaller.cs`](Modules/Shared/LoggerModule/LoggerInstaller.cs:11-14)

```csharp
public void OnRegisterServices(IServiceRegister services)
{
    services.Register<ILogger>(Logger.Instance);
}
```

**Проблема**: Регистрируется статический `ConsoleLogger`, но `FileLogger` не используется. Нет возможности конфигурации.

**Решение**:
```csharp
public void OnRegisterServices(IServiceRegister services)
{
    var logger = new FileLogger(
        "logs/game.log",
        new ConsoleLogger()
    );
    
    Logger.SetInstance(logger);
    services.Register<ILogger>(logger);
}
```

---

## 🟢 Средний приоритет

### MI-001: Строковые аллокации при форматировании
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:52-55)

```csharp
protected string GetMessage(LogLevel level, string message)
{
    return $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
}
```

**Проблема**: Каждое сообщение создаёт 3 строковые аллокации (интерполяция + `DateTime.Now` + `ToString`).

**Решение**: Использовать `StringBuilder` или `Span<char>`:
```csharp
[ThreadStatic]
private static StringBuilder? _sb;

protected string GetMessage(LogLevel level, string message)
{
    _sb ??= new StringBuilder(256);
    _sb.Clear();
    
    _sb.Append('[');
    _sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
    _sb.Append("] [");
    _sb.Append(level.ToString());
    _sb.Append("] ");
    _sb.Append(message);
    
    return _sb.ToString();
}
```

---

### MI-002: Отсутствует log rotation
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:123-148)

**Проблема**: `FileLogger` пишет в один файл без ограничения размера. Файл может стать очень большим.

**Решение**: Добавить ротацию логов:
```csharp
private void CheckRotation()
{
    if (_writer is null) return;
    
    var fileInfo = new FileInfo(_filePath);
    if (fileInfo.Length > _maxFileSize)
    {
        _writer.Close();
        _writer.Dispose();
        
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var newPath = Path.Combine(
            Path.GetDirectoryName(_filePath) ?? "",
            $"{Path.GetFileNameWithoutExtension(_filePath)}_{timestamp}.log"
        );
        
        File.Move(_filePath, newPath);
        
        _writer = new StreamWriter(_filePath, true) { AutoFlush = true };
    }
}
```

---

### MI-003: Неиспользуемый параметр конструктора Logger
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs:21)

```csharp
public abstract class Logger(ILogger logger)
```

**Проблема**: Параметр `logger` не используется.

**Решение**: Убрать параметр:
```csharp
public abstract class Logger
```

---

### MI-004: Отсутствует категоризация логов
**Файл**: [`Logger.cs`](Modules/Shared/LoggerModule/Logger.cs)

**Проблема**: Нет возможности фильтровать логи по источнику/категории.

**Решение**: Добавить категории:
```csharp
public interface ILogger : IDisposable
{
    JobHandle Log(string category, string message, LogLevel level = LogLevel.Debug);
    void SetCategoryLevel(string category, LogLevel level);
}

public class CategoryLogger : LoggerDecorator
{
    private readonly Dictionary<string, LogLevel> _categoryLevels = new();
    
    public override JobHandle Log(string category, string message, LogLevel level = LogLevel.Debug)
    {
        if (_categoryLevels.TryGetValue(category, out var catLevel) && level < catLevel)
            return JobHandle.Completed;
        
        return base.Log($"[{category}] {message}", level);
    }
}
```

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | 3 |
| Классов | 5 |
| Строк кода | ~204 |
| Критичных проблем | 2 |
| Высокий приоритет | 4 |
| Средний приоритет | 4 |

---

## 💡 Предложения по развитию

### 1. Structured Logging
Добавить поддержку структурированных логов:
```csharp
public JobHandle Log<T>(string message, T data, LogLevel level = LogLevel.Debug);
```

### 2. Log Buffering
Добавить буферизацию для уменьшения I/O:
```csharp
public class BufferedLogger : LoggerDecorator
{
    private readonly List<string> _buffer = new(1000);
    private int _bufferSize = 100;
    
    public override JobHandle Log(string message, LogLevel level)
    {
        _buffer.Add(message);
        if (_buffer.Count >= _bufferSize)
        {
            Flush();
        }
        return JobHandle.Completed;
    }
}
```

### 3. Remote Logging
Добавить отправку логов на удалённый сервер для production.

### 4. Performance Counters
Добавить счётчики производительности:
- Количество логов в секунду
- Размер буфера
- Время записи

### 5. Crash Reporting
Интеграция с системами отчётов об ошибках (Sentry, Bugsnag).

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Сделать Singleton заменяемым
- [ ] CR-002: Исправить финализатор (Dispose(false))
- [ ] HI-001: Оптимизировать запись в консоль через Channel
- [ ] HI-002: Добавить обработку ошибок при Initialize
- [ ] HI-003: Исправить race condition в Dispose
- [ ] HI-004: Добавить конфигурацию логгера
- [ ] MI-001: Оптимизировать форматирование строк
- [ ] MI-002: Добавить log rotation
- [ ] MI-003: Убрать неиспользуемый параметр
- [ ] MI-004: Добавить категоризацию логов

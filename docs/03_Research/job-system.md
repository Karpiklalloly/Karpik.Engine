# Job System Optimizations

> 📅 Обновлено: 2026-02-19

**Файл:** `Karpik.Jobs/JobSystem.cs`

---

## P0-1: CTS Pool

### Критичность
🔴 **Critical** | Блокирует production | GC pressure в каждом кадре

### Срочность
**До релиза**

### Проблема
```csharp
// Строка 153-159
var cts = new CancellationTokenSource();  // ~88 байт GC per job
```

### Решение
```csharp
// Добавить поле
private readonly ObjectPool<CancellationTokenSource> _ctsPool = 
    new(() => new CancellationTokenSource(), 1000);

// В EnqueueInternal
var cts = _ctsPool.Rent();
if (!cts.TryReset())
{
    _ctsPool.Return(cts);
    cts = new CancellationTokenSource();
}

// В ExecuteJob (finally)
cts.TryReset();
_ctsPool.Return(cts);
```

### Метрики
- **До:** ~88 байт × N jobs per frame
- **После:** 0 GC allocations в steady state

---

## P1-1: Work-Stealing Deque

### Критичность
🟡 **High** | Влияет на масштабирование

### Срочность
**До масштабного проекта**

### Проблема
```csharp
// Строка 57-65
public readonly ConcurrentQueue<JobWrapper> Queue;
```
- `ConcurrentQueue` использует `SpinWait`
- Contention при 4+ потоках

### Решение
```csharp
// Work-Stealing Deque
public class WorkStealingDeque<T>
{
    private T[] _buffer;
    private int _mask;
    private volatile int _head;
    private volatile int _tail;
    
    public void Push(T item);      // Только owner thread
    public bool Pop(out T item);   // Только owner thread  
    public bool Steal(out T item); // Другие потоки
}
```

### Метрики
- **До:** Non-linear scaling при 4+ threads
- **После:** Linear scaling до 8+ threads

---

## 🔗 Связанные

- [Performance Overview](overview.md)
- [Roadmap](../roadmap.md)

# IPC Optimizations

> 📅 Обновлено: 2026-02-19

**Файл:** `Karpik.Engine.Core/ProcessManagement/IpcProtocol.cs`

---

## P0-2: ArrayPool

### Критичность
🔴 **Critical** | Блокирует production | GC pressure

### Срочность
**До релиза**

### Проблема
```csharp
// Строка 38-44
public readonly struct IpcMessage
{
    public byte[] Payload { get; }  // Allocation на каждое сообщение
}
```

### Решение
```csharp
public readonly struct IpcMessage : IDisposable
{
    public Memory<byte> Payload { get; }
    private readonly byte[] _rentedBuffer;
    
    public IpcMessage(IpcMessageType type, int payloadSize)
    {
        _rentedBuffer = ArrayPool<byte>.Shared.Rent(5 + payloadSize);
        Payload = _rentedBuffer.AsMemory(5, payloadSize);
        _rentedBuffer[4] = (byte)type;
    }
    
    public void Dispose()
    {
        if (_rentedBuffer != null)
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
    }
}
```

### Альтернатива (Zero-Allocation)
```csharp
// Использовать MemoryPool<byte>.Shared для длинных сообщений
using var owner = MemoryPool<byte>.Shared.Rent(size);
```

### Метрики
- **До:** N × payloadSize байт GC per frame
- **После:** 0 GC allocations

---

## 🔗 Связанные

- [Performance Overview](overview.md)
- [Roadmap](../roadmap.md)
- [Process Isolation](../../plans/process-isolation-architecture.md)

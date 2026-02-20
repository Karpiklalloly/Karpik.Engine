# Code Review: Network.Shared.LiteNetLib

**Module:** Network.Shared.LiteNetLib  
**Type:** Network Implementation (LiteNetLib wrapper)  
**Status:** ✅ Good - Minor issues

---

## Overview

Реализация сетевых интерфейсов на базе LiteNetLib. Содержит обёртки для NetManager, NetPeer, NetPacketReader, NetDataWriter.

---

## Statistics

| Metric | Value |
|--------|-------|
| Files | 6 |
| Classes | 6 |
| Lines of Code | ~350 |

---

## Issues Found

### HI-1: FirstPeer бросает исключение если нет пиров [High]

**File:** [`LiteNetLibNetworkManager.cs`](Modules/Shared/Network.Shared.LiteNetLib/LiteNetLibNetworkManager.cs:33)

```csharp
public IPeer FirstPeer => _peers[Manager.FirstPeer];
```

**Problem:** Если нет подключенных пиров, `Manager.FirstPeer` вернёт null, а `_peers[null]` бросит `KeyNotFoundException`.

**Solution:** Добавить проверку:
```csharp
public IPeer? FirstPeer => Manager.FirstPeer is not null && _peers.TryGetValue(Manager.FirstPeer, out var peer) 
    ? peer 
    : null;
```

---

### HI-2: Memory leak — NetPacketReader не всегда recycled [High]

**File:** [`LiteNetLibNetworkManager.cs`](Modules/Shared/Network.Shared.LiteNetLib/LiteNetLibNetworkManager.cs:80-88)

```csharp
private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, global::LiteNetLib.DeliveryMethod deliveryMethod)
{
    if (!_peers.TryGetValue(peer, out var wrappedPeer))
    {
        wrappedPeer = new LiteNetLibPeer(peer);
        _peers.TryAdd(peer, wrappedPeer);
    }
    NetworkReceiveEvent?.Invoke(wrappedPeer, new LiteNetLibReader(reader), channel, (DeliveryMethod)deliveryMethod);
    // reader не recycled! LiteNetLibReader.Recycle() должен вызываться потребителем
}
```

**Problem:** LiteNetLibReader оборачивает NetPacketReader, но ответственность за Recycle() перенесена на потребителя. Если потребитель забудет — memory leak.

**Solution:** Документировать или автоматически recycled:
```csharp
// Вариант 1: Автоматический recycle после события
try
{
    NetworkReceiveEvent?.Invoke(...);
}
finally
{
    reader.Recycle();
}

// Вариант 2: Владение передаётся потребителю (документировать)
/// <summary>
/// Consumer MUST call IReader.Recycle() after processing!
/// </summary>
```

---

### HI-3: Cast delivery method — potential invalid values [High]

**File:** [`LiteNetLibNetworkManager.cs`](Modules/Shared/Network.Shared.LiteNetLib/LiteNetLibNetworkManager.cs:72)

```csharp
public void SendToAll(IWriter writer, DeliveryMethod deliveryMethod)
{
    Manager.SendToAll(((LiteNetLibWriter)writer).Writer, (global::LiteNetLib.DeliveryMethod)deliveryMethod);
}
```

**Problem:** Прямой cast между enum'ами. Если значения не совпадают — undefined behavior.

**Solution:** Использовать маппинг или убедиться в совпадении значений:
```csharp
private static global::LiteNetLib.DeliveryMethod ToLiteNetLib(DeliveryMethod method) => method switch
{
    DeliveryMethod.Unreliable => global::LiteNetLib.DeliveryMethod.Unreliable,
    DeliveryMethod.ReliableUnordered => global::LiteNetLib.DeliveryMethod.ReliableUnordered,
    DeliveryMethod.ReliableOrdered => global::LiteNetLib.DeliveryMethod.ReliableOrdered,
    _ => throw new ArgumentOutOfRangeException(nameof(method))
};
```

---

### MI-1: ConcurrentDictionary для пиров — overhead [Medium]

**File:** [`LiteNetLibNetworkManager.cs`](Modules/Shared/Network.Shared.LiteNetLib/LiteNetLibNetworkManager.cs:21)

```csharp
private readonly ConcurrentDictionary<NetPeer, IPeer> _peers = new();
```

**Problem:** ConcurrentDictionary используется, но LiteNetLib вызывает события в одном потоке (после PollEvents).

**Solution:** Если PollEvents всегда вызывается из одного потока — использовать Dictionary:
```csharp
private readonly Dictionary<NetPeer, IPeer> _peers = new();
```

---

### MI-2: LiteNetLibWriter не имеет Pool [Medium]

**File:** [`LiteNetLibWriter.cs`](Modules/Shared/Network.Shared.LiteNetLib/LiteNetLibWriter.cs:6)

```csharp
public class LiteNetLibWriter : IWriter
{
    public NetDataWriter Writer { get; }
    
    public LiteNetLibWriter(NetDataWriter writer)
    {
        Writer = writer;
    }
}
```

**Problem:** Каждый вызов `CreateWriter()` создаёт новый NetDataWriter. В высоконагруженном сетевом коде это много аллокаций.

**Solution:** Использовать пул:
```csharp
private readonly ObjectPool<NetDataWriter> _writerPool = new(() => new NetDataWriter());

public IWriter CreateWriter()
{
    return new LiteNetLibWriter(_writerPool.Get());
}

public void ReturnWriter(IWriter writer)
{
    _writerPool.Return(((LiteNetLibWriter)writer).Writer);
}
```

---

### MI-3: Stop() не очищает события полностью [Medium]

**File:** [`LiteNetLibNetworkManager.cs`](Modules/Shared/Network.Shared.LiteNetLib/LiteNetLibNetworkManager.cs:57-68)

```csharp
public void Stop()
{
    _listener.NetworkReceiveEvent -= OnNetworkReceive;
    _listener.PeerConnectedEvent -= OnPeerConnected;
    _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
    _listener.ConnectionRequestEvent -= ListenerOnConnectionRequestEvent;
    _listener.ClearNetworkReceiveEvent();
    _listener.ClearNetworkReceiveUnconnectedEvent();
    // ClearNetworkReceiveEvent() уже очищает NetworkReceiveEvent!
    Manager.DisconnectAll();
    Manager.Stop();
    _peers.Clear();
}
```

**Problem:** Сначала отписываемся от событий, потом вызываем ClearNetworkReceiveEvent() — дублирование.

**Solution:** Использовать либо отписку, либо Clear:
```csharp
public void Stop()
{
    // Вариант 1: Только Clear
    _listener.ClearNetworkReceiveEvent();
    _listener.ClearNetworkReceiveUnconnectedEvent();
    
    // Вариант 2: Только отписка
    _listener.NetworkReceiveEvent -= OnNetworkReceive;
    // ...
}
```

---

## Positive Aspects ✅

1. **Чистые обёртки** — простые классы-адаптеры без лишней логики
2. **Правильная реализация IDisposable паттерна** — Stop() очищает ресурсы
3. **Thread-safe** — ConcurrentDictionary (хотя и с overhead)
4. **GetFreePort()** — полезный utility-метод

---

## Recommendations

| Priority | Action |
|----------|--------|
| **High** | Добавить null-check для FirstPeer |
| **High** | Документировать ответственность за Recycle() |
| **High** | Добавить safe cast для DeliveryMethod |
| Medium | Рассмотреть ObjectPool для Writer |
| Medium | Убрать дублирование очистки событий в Stop() |

---

## Verdict

**Хороший модуль.** Чистые обёртки над LiteNetLib. Основные проблемы — потенциальный null для FirstPeer и ответственность за Recycle(). В остальном — качественная реализация.

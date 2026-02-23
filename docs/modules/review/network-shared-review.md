# Network.Shared - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Отсутствие исходных файлов
**Директория**: [`Modules/Shared/Network.Shared`](Modules/Shared/Network.Shared)

**Проблема**: Директория содержит только `.csproj` файл без исходных кодов. Абстракции сетевого модуля не реализованы.

**Решение**: Добавить базовые интерфейсы:
```csharp
// INetworkManager.cs
public interface INetworkManager
{
    void Start(int port);
    void Stop();
    void Connect(string address, int port, string key);
    void PollEvents();
    int GetFreePort();
    
    event Action<IPeer, IReader, byte, DeliveryMethod> NetworkReceiveEvent;
    event Action<IPeer> PeerConnectedEvent;
    event Action<IPeer, IDisconnectInfo> PeerDisconnectedEvent;
}

// IPeer.cs
public interface IPeer
{
    int Id { get; }
    EndPoint EndPoint { get; }
    void Send(byte[] data, DeliveryMethod method);
    void Disconnect();
}

// IReader.cs / IWriter.cs
public interface IReader { byte ReadByte(); int ReadInt(); ... }
public interface IWriter { void WriteByte(byte b); void WriteInt(int i); ... }
```

---

## 🟡 Высокий приоритет

### HI-001: Отсутствие документации API
**Проблема**: Нет документации по контрактам между клиентом и сервером.

**Решение**: Создать `README.md` с описанием протокола.

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | 1 (только .csproj) |
| Критичных проблем | 1 |
| Высокий приоритет | 1 |

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Добавить базовые интерфейсы для сетевого модуля
- [ ] HI-001: Создать документацию API

# Network.Client - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Пустые обработчики событий
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs:30-43)

```csharp
private void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
{
    
}

private void ManagerOnPeerConnectedEvent(IPeer peer)
{
    
}

private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
{
    
}
```

**Проблема**: Обработчики сетевых событий пустые. Модуль не обрабатывает входящие пакеты, подключения и отключения. Это делает сетевой клиент нефункциональным.

**Решение**: Реализовать обработку событий:
```csharp
private void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
{
    // Чтение типа пакета
    var packetType = reader.ReadByte();
    
    // Диспетчеризация по типу пакета
    _packetHandlers.TryGetValue(packetType, out var handler);
    handler?.Handle(peer, reader);
}

private void ManagerOnPeerConnectedEvent(IPeer peer)
{
    Logger.Instance.Log("Network", $"Connected to server: {peer.EndPoint}");
    // Отправка handshake
}

private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
{
    Logger.Instance.Log("Network", $"Disconnected: {info.Reason}");
    // Попытка переподключения
}
```

---

### CR-002: Отсутствие обработки ошибок подключения
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs:13-19)

```csharp
public void Init()
{
    _manager.Start(_manager.GetFreePort());
    _manager.Connect(_config.Address, _config.Port, _config.Key);
    // ...
}
```

**Проблема**: Нет обработки ошибок при подключении. Если сервер недоступен, исключение не перехватывается.

**Решение**:
```csharp
public void Init()
{
    try
    {
        _manager.Start(_manager.GetFreePort());
        _manager.Connect(_config.Address, _config.Port, _config.Key);
    }
    catch (Exception ex)
    {
        Logger.Instance.Log("Network", $"Failed to connect: {ex.Message}", LogLevel.Error);
        // Попытка переподключения через delay
    }
}
```

---

## 🟡 Высокий приоритет

### HI-001: Конфиг создаётся в поле класса
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs:11)

```csharp
private readonly NetworkConfig _config = new();
```

**Проблема**: Конфигурация создаётся напрямую, а не внедряется через DI. Это затрудняет тестирование и изменение конфигурации.

**Решение**:
```csharp
[DI] private NetworkConfig _config = null!;
```

---

### HI-002: Отсутствует состояние подключения
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs)

**Проблема**: Нет отслеживания состояния подключения (connecting, connected, disconnected). Нельзя узнать, подключен ли клиент.

**Решение**:
```csharp
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
```

---

### HI-003: Нет повторного подключения
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs)

**Проблема**: При разрыве соединения нет автоматического переподключения.

**Решение**:
```csharp
private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
{
    State = ConnectionState.Disconnected;
    
    if (_autoReconnect)
    {
        State = ConnectionState.Reconnecting;
        _reconnectAttempts++;
        
        if (_reconnectAttempts < _maxReconnectAttempts)
        {
            Task.Delay(_reconnectDelay).ContinueWith(_ => 
            {
                _manager.Connect(_config.Address, _config.Port, _config.Key);
            });
        }
    }
}
```

---

### HI-004: Неправильное имя модуля
**Файл**: [`NetworkClientInstaller.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/NetworkClientInstaller.cs:9)

```csharp
public string Name => "Network.Client.Core";
```

**Проблема**: Имя модуля указывает на `Core`, но это `LiteNetLib` реализация.

**Решение**:
```csharp
public string Name => "Network.Client.LiteNetLib";
```

---

## 🟢 Средний приоритет

### MI-001: Пустой OnRegisterServices
**Файл**: [`NetworkClientInstaller.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/NetworkClientInstaller.cs:11-14)

```csharp
public void OnRegisterServices(IServiceRegister services)
{
    
}
```

**Проблема**: Метод пустой. Сетевые сервисы не регистрируются.

**Решение**: Регистрировать конфигурацию и другие сервисы:
```csharp
public void OnRegisterServices(IServiceRegister services)
{
    services.RegisterSingleton(new NetworkConfig());
}
```

---

### MI-002: Отсутствие таймаута подключения
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs:16)

```csharp
_manager.Connect(_config.Address, _config.Port, _config.Key);
```

**Проблема**: Нет таймаута для подключения. Клиент может ждать бесконечно.

**Решение**:
```csharp
// В UpdateNetworkClientSystem
private float _connectionTimeout = 10f;
private float _connectionTimer;

public void Run()
{
    _manager.PollEvents();
    
    if (State == ConnectionState.Connecting)
    {
        _connectionTimer += Time.DeltaTime;
        if (_connectionTimer > _connectionTimeout)
        {
            Logger.Instance.Log("Network", "Connection timeout", LogLevel.Warning);
            State = ConnectionState.Disconnected;
            // Попытка переподключения
        }
    }
}
```

---

### MI-003: Нет логирования сетевых событий
**Файл**: [`InitNetworkClientSystem.cs`](Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs)

**Проблема**: Отсутствует логирование важных сетевых событий для отладки.

**Решение**: Добавить логирование:
```csharp
public void Init()
{
    Logger.Instance.Log("Network", $"Connecting to {_config.Address}:{_config.Port}...");
    // ...
}
```

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | 5 |
| Классов | 4 |
| Строк кода | ~100 |
| Критичных проблем | 2 |
| Высокий приоритет | 4 |
| Средний приоритет | 3 |

---

## 💡 Предложения по развитию

### 1. Packet System
Реализовать систему пакетов:
```csharp
public interface IPacket
{
    byte PacketId { get; }
    void Serialize(IWriter writer);
    void Deserialize(IReader reader);
}

public interface IPacketHandler
{
    byte PacketId { get; }
    void Handle(IPeer peer, IReader reader);
}
```

### 2. Connection State Machine
Добавить конечный автомат состояний подключения.

### 3. Network Statistics
Добавить сбор статистики:
- RTT (Round Trip Time)
- Packet loss
- Bandwidth usage

### 4. Message Queue
Добавить очередь сообщений для надёжной доставки.

### 5. Network Discovery
Добавить автоматический поиск серверов в локальной сети.

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Реализовать обработчики сетевых событий
- [ ] CR-002: Добавить обработку ошибок подключения
- [ ] HI-001: Внедрять конфигурацию через DI
- [ ] HI-002: Добавить отслеживание состояния подключения
- [ ] HI-003: Реализовать автоматическое переподключение
- [ ] HI-004: Исправить имя модуля
- [ ] MI-001: Регистрировать сервисы в OnRegisterServices
- [ ] MI-002: Добавить таймаут подключения
- [ ] MI-003: Добавить логирование сетевых событий

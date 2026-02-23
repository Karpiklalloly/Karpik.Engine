# Code Review: Network.Server.LiteNetLib

**Module:** Network.Server.LiteNetLib  
**Type:** Server Network Implementation  
**Status:** ⚠️ Needs Work - Placeholder code

---

## Overview

Network.Server.LiteNetLib — серверная реализация сетевого модуля на базе LiteNetLib. Содержит системы инициализации, обновления и уничтожения сетевого соединения.

---

## Statistics

| Metric | Value |
|--------|-------|
| Files | 3 |
| Classes | 4 |
| Lines of Code | ~110 |

---

## Issues Found

### CR-1: Пустые обработчики событий [Critical]

**File:** [`UpdateSystem.cs`](Modules/Server/Network.Server/Network.Server.LiteNetLib/Systems/UpdateSystem.cs:28-46)

```csharp
private void ManagerOnConnectionRequestEvent(IConnectionRequest request)
{
    // Пусто - никто не может подключиться!
}

private void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
{
    // Пусто - данные не обрабатываются!
}

private void ManagerOnPeerConnectedEvent(IPeer peer)
{
    // Пусто - игрок не получает подтверждения!
}

private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
{
    // Пусто - очистка не выполняется!
}
```

**Problem:** Все обработчики событий пустые. Сервер не обрабатывает подключения, не принимает данные, не очищает ресурсы при отключении.

**Solution:** Реализовать базовую логику:
```csharp
private void ManagerOnConnectionRequestEvent(IConnectionRequest request)
{
    // Валидация, лимиты игроков, бан-лист
    if (CanAcceptPlayer())
    {
        request.Accept();
    }
    else
    {
        request.Reject();
    }
}

private void ManagerOnPeerConnectedEvent(IPeer peer)
{
    // Создать entity для игрока, отправить начальное состояние
    _world.NewEntity().Add<PlayerComponent>().PeerId = peer.Id;
}
```

---

### HI-1: Hardcoded порт [High]

**File:** [`UpdateSystem.cs`](Modules/Server/Network.Server/Network.Server.LiteNetLib/Systems/UpdateSystem.cs:13)

```csharp
public void Init()
{
    _manager.Start(9051); // Hardcoded!
}
```

**Problem:** Порт захардкожен. Невозможно изменить без перекомпиляции.

**Solution:** Использовать конфигурацию:
```csharp
[DI] private ServerConfig _config = null!;

public void Init()
{
    _manager.Start(_config.Port);
}
```

---

### HI-2: Отсутствует DI для INetworkManager [High]

**File:** [`UpdateSystem.cs`](Modules/Server/Network.Server/Network.Server.LiteNetLib/Systems/UpdateSystem.cs:9)

```csharp
[DI] private INetworkManager _manager = null!;
```

**Problem:** INetworkManager должен быть зарегистрирован в DI, но инсталлер этого модуля не регистрирует сервисы.

**Solution:** Добавить регистрацию в NetworkServerInstaller:
```csharp
public void OnRegisterServices(IServiceRegister services)
{
    services.AddSingleton<INetworkManager, LiteNetLibNetworkManager>();
}
```

---

### HI-3: Неправильное именование класса [High]

**File:** [`NetworkServerModule.cs`](Modules/Server/Network.Server/Network.Server.LiteNetLib/NetworkServerModule.cs:12)

```csharp
internal class NetworkServerModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new InitNetworkClientSystem())    // Client? Это сервер!
            .Add(new UpdateNetworkClientSystem())  // Client? Это сервер!
            .Add(new DestroyNetworkClientSystem()); // Client? Это сервер!
    }
}
```

**Problem:** Copy-paste из клиентского модуля. Имена классов не соответствуют назначению.

**Solution:** Переименовать:
```csharp
.Add(new InitNetworkServerSystem())
.Add(new UpdateNetworkServerSystem())
.Add(new DestroyNetworkServerSystem());
```

---

### MI-1: Нет обработки ошибок [Medium]

**File:** [`UpdateSystem.cs`](Modules/Server/Network.Server/Network.Server.LiteNetLib/Systems/UpdateSystem.cs:11-18)

```csharp
public void Init()
{
    _manager.Start(9051);
    _manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
    // Нет try-catch, нет проверки на null
}
```

**Problem:** Если _manager null или порт занят — исключение не обработано.

**Solution:**
```csharp
public void Init()
{
    Debug.Assert(_manager is not null, "INetworkManager not injected");
    try
    {
        _manager.Start(_config.Port);
    }
    catch (Exception ex)
    {
        Logger.Instance.Log(nameof(InitNetworkServerSystem), $"Failed to start server: {ex}", LogLevel.Error);
        throw;
    }
}
```

---

## Positive Aspects ✅

1. **Правильная отписка от событий** — в Destroy() корректно удаляются обработчики
2. **Разделение на системы** — Init/Update/Destroy разделены по responsibility
3. **Использование DI** — правильный паттерн инъекции зависимостей

---

## Recommendations

| Priority | Action |
|----------|--------|
| **Critical** | Реализовать обработчики событий |
| **Critical** | Добавить регистрацию INetworkManager в DI |
| **High** | Переименовать классы (Client → Server) |
| **High** | Вынести порт в конфигурацию |
| Medium | Добавить обработку ошибок |

---

## Verdict

**Модуль в зачаточном состоянии.** Код скопирован из клиентского модуля и не адаптирован. Требуется полная реализация серверной логики.

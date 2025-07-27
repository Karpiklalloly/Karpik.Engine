# RPC System Usage Guide

## Обзор

Система RPC в KarpikEngine поддерживает два типа удаленных вызовов:

- **TargetRpc** - вызовы с сервера для конкретного клиента
- **ClientRpc** - вызовы с сервера для всех клиентов

## Управление ID команд

Все генераторы используют единый счетчик команд через `CommandIdManager`, что гарантирует отсутствие конфликтов между:
- Обычными командами (IEventCommand, IStateCommand)
- TargetRpc методами
- ClientRpc методами

ID назначаются автоматически в порядке обнаружения и остаются стабильными между компиляциями.

## TargetRpc

### Определение методов

```csharp
public partial class PlayerController
{
    [TargetRpc]
    public void ShowMessage(string message, float duration)
    {
        // Реализация будет выполнена на клиенте
    }
}
```

### Использование на сервере

```csharp
// Генерируется автоматически в Game.Generated.Server
TargetRpcSender.ShowMessage(targetPeer, "Hello World!", 3.0f);
```

### Обработка на клиенте

```csharp
public partial class TargetRpcDispatcher
{
    partial void OnShowMessage(string message, float duration)
    {
        // Ваша реализация обработки
        UI.ShowNotification(message, duration);
    }
}
```

## ClientRpc

### Определение методов

```csharp
public partial class GameManager
{
    [ClientRpc]
    public void GameStarted(int gameMode, string mapName)
    {
        // Реализация будет выполнена на всех клиентах
    }
}
```

### Использование на сервере

```csharp
// Отправка конкретному клиенту
ClientRpcSender.GameStarted(targetPeer, 1, "Arena");

// Отправка всем клиентам
ClientRpcSender.GameStartedAll(netManager, 1, "Arena");
```

### Обработка на клиенте

```csharp
public partial class ClientRpcDispatcher
{
    partial void OnGameStarted(int gameMode, string mapName)
    {
        // Ваша реализация обработки
        GameUI.ShowGameStart(gameMode, mapName);
    }
}
```

## Поддерживаемые типы параметров

- `int`, `uint`, `short`, `ushort`, `byte`
- `float`, `double`
- `bool`
- `string`
- Другие типы можно добавить в `GetReaderMethod`

## Интеграция с существующей системой

RPC методы используют тот же `PacketType.Command` и диспетчеризацию, что и существующие команды, но с разными диапазонами ID:

- Команды: 1-999
- TargetRpc: 1000-1999  
- ClientRpc: 2000-2999

## Пример полной интеграции

### Сервер
```csharp
// В игровой логике сервера
public class ServerGameLogic
{
    public void OnPlayerTakeDamage(NetPeer playerPeer, int damage)
    {
        // Обновляем здоровье игрока
        var newHealth = CalculateHealth(damage);
        
        // Отправляем обновление конкретному игроку
        TargetRpcSender.UpdateHealth(playerPeer, newHealth, 100);
        
        // Уведомляем всех о событии
        ClientRpcSender.PlayerTookDamageAll(netManager, playerId, damage);
    }
}
```

### Клиент
```csharp
public partial class ClientRpcDispatcher
{
    partial void OnUpdateHealth(int newHealth, int maxHealth)
    {
        PlayerUI.UpdateHealthBar(newHealth, maxHealth);
    }
    
    partial void OnPlayerTookDamage(int playerId, int damage)
    {
        ShowDamageEffect(playerId, damage);
    }
}
```

## Архитектура с RPC командами (аналогично ServerRPC)

### Размещение кода:
- **KarpikEngineShared** - структуры RPC команд
- **KarpikEngineServer.Generated** - автогенерируемые отправители RPC
- **KarpikEngineClient.Generated** - автогенерируемые диспетчеры RPC
- **KarpikEngineClient** - ECS системы для обработки команд

### 1. Создание RPC команд в Shared:
```csharp
// KarpikEngineShared/RpcCommands.cs

// TargetRpc команды - отправляются конкретному клиенту
public struct ShowMessageTargetRpc : ITargetRpcCommand
{
    public string message;
    public float duration;
}

public struct UpdateHealthTargetRpc : ITargetRpcCommand
{
    public int health;
    public int maxHealth;
}

// ClientRpc команды - отправляются всем клиентам
public struct PlayerJoinedClientRpc : IClientRpcCommand
{
    public string playerName;
    public int playerId;
}

public struct GameStartedClientRpc : IClientRpcCommand
{
    public string mapName;
    public int gameMode;
}
```

### 2. Использование на сервере:
```csharp
// KarpikEngineServer - автогенерируемые отправители
using KarpikEngineServer.Generated;
using KarpikEngineShared;

// TargetRpc - конкретному игроку
var showMessageCmd = new ShowMessageTargetRpc
{
    message = "Welcome!",
    duration = 5.0f
};
TargetClientRpcSender.Instance.ShowMessage(playerPeer, showMessageCmd);

// ClientRpc - всем игрокам
var playerJoinedCmd = new PlayerJoinedClientRpc
{
    playerName = "NewPlayer",
    playerId = 123
};
TargetClientRpcSender.Instance.PlayerJoinedAll(netManager, playerJoinedCmd);
```

### 3. Обработка на клиенте через ECS системы:
```csharp
// KarpikEngineClient/RpcHandlingSystems.cs
public class TargetRpcHandlingSystem : IEcsRun
{
    public void Run(EcsWorld world)
    {
        // Обработка ShowMessageTargetRpc
        foreach (var entity in world.Where(EcsStaticMask.Inc<ShowMessageTargetRpc>().Build()))
        {
            ref var cmd = ref world.GetPool<ShowMessageTargetRpc>().Get(entity);
            Console.WriteLine($"[CLIENT] Message: '{cmd.message}' ({cmd.duration}s)");
            
            // UI.ShowNotification(cmd.message, cmd.duration);
            world.GetPool<ShowMessageTargetRpc>().Del(entity);
        }

        // Обработка UpdateHealthTargetRpc
        foreach (var entity in world.Where(EcsStaticMask.Inc<UpdateHealthTargetRpc>().Build()))
        {
            ref var cmd = ref world.GetPool<UpdateHealthTargetRpc>().Get(entity);
            Console.WriteLine($"[CLIENT] Health: {cmd.health}/{cmd.maxHealth}");
            
            // HealthBar.UpdateHealth(cmd.health, cmd.maxHealth);
            world.GetPool<UpdateHealthTargetRpc>().Del(entity);
        }
    }
}
```

### Результат в консоли клиента:
```
[CLIENT] Message: 'Welcome!' (5s)
[CLIENT] Health: 80/100
[CLIENT] Player joined: NewPlayer (ID: 123)
[CLIENT] Game started! Map: Arena_01, Mode: 1
```

### Преимущества новой архитектуры:
- **Единообразие** - работает аналогично ServerRPC и обычным командам
- **Структуры команд** - четкое определение данных через struct
- **ECS интеграция** - команды автоматически отправляются в EcsEventWorld
- **Автоматическая сериализация** - генератор создает код сериализации полей
- **Единый счетчик ID** - CommandIdManager для всех команд
- **Правильное разделение** - код размещен в Client/Server/Shared проектах

## Старая архитектура с пользовательскими ECS событиями (Legacy)

### Размещение кода:
- **KarpikEngineShared** - пользовательские ECS события и RPC методы, принимающие их как параметры
- **KarpikEngineServer** - автогенерируемые отправители RPC
- **KarpikEngineClient** - автогенерируемые диспетчеры RPC и ECS системы для обработки событий

### 1. Создание ECS событий в Shared:
```csharp
// KarpikEngineShared/RpcExamples.cs
using DCFApixels.DragonECS;

// Пользователь сам создает ECS компоненты-события
public struct ShowMessageEvent : IEcsComponentEvent
{
    public string message;
    public float duration;
}

public struct UpdateHealthEvent : IEcsComponentEvent
{
    public int health;
    public int maxHealth;
}

public struct PlayerJoinedEvent : IEcsComponentEvent
{
    public string playerName;
    public int playerId;
}

public struct GameStartedEvent : IEcsComponentEvent
{
    public string mapName;
    public int gameMode;
}
```

### 2. Объявление RPC методов в Shared:
```csharp
// RPC методы принимают ECS события как параметры
public partial class PlayerNotifications
{
    [TargetRpc]
    public void ShowMessage(ShowMessageEvent eventData) { }
    
    [TargetRpc] 
    public void UpdateHealth(UpdateHealthEvent eventData) { }
}

public partial class GameEvents
{
    [ClientRpc]
    public void PlayerJoined(PlayerJoinedEvent eventData) { }
    
    [ClientRpc]
    public void GameStarted(GameStartedEvent eventData) { }
}
```

### 3. Использование на сервере:
```csharp
// KarpikEngineServer - автогенерируемые отправители
using KarpikEngineServer.Generated;
using KarpikEngineShared;

// TargetRpc - конкретному игроку
var showMessageEvent = new ShowMessageEvent
{
    message = "Welcome!",
    duration = 5.0f
};
TargetRpcSender.ShowMessage(playerPeer, showMessageEvent);

var updateHealthEvent = new UpdateHealthEvent
{
    health = 80,
    maxHealth = 100
};
TargetRpcSender.UpdateHealth(playerPeer, updateHealthEvent);

// ClientRpc - всем игрокам
var playerJoinedEvent = new PlayerJoinedEvent
{
    playerName = "NewPlayer",
    playerId = 123
};
ClientRpcSender.PlayerJoinedAll(netManager, playerJoinedEvent);

var gameStartedEvent = new GameStartedEvent
{
    mapName = "Arena_01",
    gameMode = 1
};
ClientRpcSender.GameStartedAll(netManager, gameStartedEvent);
```

### 4. Обработка на клиенте через ECS системы:
```csharp
// KarpikEngineClient/RpcEventSystems.cs
public class TargetRpcEventSystem : IEcsRun
{
    public void Run(EcsWorld world)
    {
        // Обработка ShowMessageEvent
        foreach (var entity in world.Where(EcsStaticMask.Inc<ShowMessageEvent>().Build()))
        {
            ref var evt = ref world.GetPool<ShowMessageEvent>().Get(entity);
            Console.WriteLine($"[CLIENT] Message: '{evt.message}' ({evt.duration}s)");
            
            // UI.ShowNotification(evt.message, evt.duration);
            world.GetPool<ShowMessageEvent>().Del(entity);
        }

        // Обработка UpdateHealthEvent
        foreach (var entity in world.Where(EcsStaticMask.Inc<UpdateHealthEvent>().Build()))
        {
            ref var evt = ref world.GetPool<UpdateHealthEvent>().Get(entity);
            Console.WriteLine($"[CLIENT] Health: {evt.health}/{evt.maxHealth}");
            
            // HealthBar.UpdateHealth(evt.health, evt.maxHealth);
            world.GetPool<UpdateHealthEvent>().Del(entity);
        }
    }
}
```

### Результат в консоли клиента:
```
[CLIENT] Message: 'Welcome!' (5s)
[CLIENT] Health: 80/100
[CLIENT] Player joined: NewPlayer (ID: 123)
[CLIENT] Game started! Map: Arena_01, Mode: 1
```

### Преимущества новой архитектуры:
- **Пользовательский контроль** - разработчик сам создает и контролирует структуру ECS событий
- **ECS интеграция** - RPC события автоматически отправляются в EcsEventWorld на клиенте
- **Разделение проектов** - код размещен в правильных проектах (Client/Server/Shared)
- **Типобезопасность** - строгая типизация через пользовательские ECS события
- **Единый счетчик ID** - все команды используют общий CommandIdManager
- **Гибкость сериализации** - пользователь может реализовать собственную логику сериализации
### Как р
аботает генератор:

1. **Сканирует RPC методы** с атрибутами `[TargetRpc]` и `[ClientRpc]`
2. **Проверяет параметры** - метод должен принимать один параметр типа `IEcsComponentEvent`
3. **Анализирует поля** события для автоматической сериализации
4. **Генерирует код**:
   - Отправители на сервере с прямой сериализацией полей
   - Диспетчеры на клиенте с прямой десериализацией полей
   - Автоматическая отправка событий в `EcsEventWorld`

### Автоматическая сериализация полей:

Для события:
```csharp
public struct ShowMessageEvent : IEcsComponentEvent
{
    public string message;
    public float duration;
}
```

Генератор создает:
```csharp
// На сервере (отправка)
_writer.Put(eventData.message);
_writer.Put(eventData.duration);

// На клиенте (получение)
eventData.message = reader.GetString();
eventData.duration = reader.GetFloat();
```

### Преимущества архитектуры:
- **Пользовательские события** - разработчик сам создает ECS события
- **ECS интеграция** - события автоматически отправляются в EcsEventWorld
- **Автоматическая сериализация** - генератор создает код сериализации всех полей
- **Типобезопасность** - строгая типизация через пользовательские структуры
- **Единый счетчик ID** - CommandIdManager для всех команд
- **Правильное разделение** - код размещен в Client/Server/Shared проектах
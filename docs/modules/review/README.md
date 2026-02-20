# Code Reviews Index

Полный индекс код-ревью модулей KarpikEngine.

---

## Statistics

| Module | Critical | High | Medium | Low | Status |
|--------|----------|------|--------|-----|--------|
| [Graphics.Raylib](graphics-raylib-review.md) | 2 | 3 | 2 | 0 | ⚠️ Needs Work |
| [Graphics.Core](graphics-core-review.md) | 0 | 0 | 2 | 2 | ✅ Good |
| [Input](input-review.md) | 2 | 4 | 3 | 0 | ⚠️ Needs Work |
| [Network.Client](network-client-review.md) | 2 | 4 | 3 | 0 | ⚠️ Needs Work |
| [Network.Server](network-server-review.md) | 2 | 3 | 1 | 0 | ⚠️ Needs Work |
| [Network.Shared](network-shared-review.md) | 1 | 1 | 0 | 0 | ⚠️ Needs Work |
| [Network.Shared.LiteNetLib](network-shared-litenetlib-review.md) | 0 | 3 | 3 | 0 | ✅ Good |
| [AssetManagement](asset-management-review.md) | 2 | 4 | 4 | 0 | ⚠️ Needs Work |
| [Logger](logger-review.md) | 2 | 4 | 4 | 0 | ⚠️ Needs Work |
| [UIToolkit](uitoolkit-review.md) | 2 | 4 | 4 | 0 | ⚠️ Needs Work |
| [Modding](modding-review.md) | 2 | 4 | 2 | 0 | ⚠️ Needs Work |
| [StatAndAbilities](stat-and-abilities-review.md) | 0 | 3 | 3 | 0 | ✅ Good |
| [Tween.Core](tween-review.md) | 0 | 2 | 3 | 0 | ✅ Good |
| **TOTAL** | **17** | **39** | **34** | **2** | |

---

## By Category

### Client Modules

| Module | Description | Status |
|--------|-------------|--------|
| [Graphics.Raylib](graphics-raylib-review.md) | Raylib backend implementation | ⚠️ |
| [Graphics.Core](graphics-core-review.md) | Graphics abstraction interfaces | ✅ |
| [Input](input-review.md) | Input handling system | ⚠️ |
| [UIToolkit](uitoolkit-review.md) | UI framework | ⚠️ |

### Server Modules

| Module | Description | Status |
|--------|-------------|--------|
| [Network.Server](network-server-review.md) | Server network logic | ⚠️ |

### Shared Modules

| Module | Description | Status |
|--------|-------------|--------|
| [Network.Client](network-client-review.md) | Client network logic | ⚠️ |
| [Network.Shared](network-shared-review.md) | Network abstractions | ⚠️ |
| [Network.Shared.LiteNetLib](network-shared-litenetlib-review.md) | LiteNetLib implementation | ✅ |
| [AssetManagement](asset-management-review.md) | Asset loading system | ⚠️ |
| [Logger](logger-review.md) | Logging system | ⚠️ |
| [Modding](modding-review.md) | Lua modding system | ⚠️ |
| [StatAndAbilities](stat-and-abilities-review.md) | RPG stats system | ✅ |
| [Tween.Core](tween-review.md) | Animation system | ✅ |

---

## Top Critical Issues

### 1. Null-forgiving operators без проверок
**Modules:** Graphics.Raylib, Input, Network.Client, Logger, UIToolkit, Modding

```csharp
[DI] private IServiceProvider _serviceProvider = null!;
// Используется без проверки на null
```

**Solution:** С NRT это допустимо, но рекомендуется добавить Debug.Assert:
```csharp
public void Init()
{
    Debug.Assert(_serviceProvider is not null, "DI injection failed");
}
```

---

### 2. Пустые обработчики событий
**Modules:** Network.Client, Network.Server

```csharp
private void ManagerOnPeerConnectedEvent(IPeer peer)
{
    // Пусто!
}
```

**Solution:** Реализовать базовую логику или выбросить `NotImplementedException`.

---

### 3. Singleton без возможности замены
**Modules:** Logger, StatAndAbilities

```csharp
public static Logger Instance { get; } = new();
```

**Solution:** Инжектировать через DI для тестируемости.

---

### 4. GetAwaiter().GetResult() — Deadlock risk
**Module:** Modding

```csharp
Log($"Error: {ex}", LogLevel.Error).GetAwaiter().GetResult();
```

**Solution:** Использовать синхронный логгинг или fire-and-forget.

---

### 5. Race conditions
**Module:** AssetManagement

```csharp
// LoadAssetAsync и GetAsset могут конфликтовать
```

**Solution:** Использовать async-await или блокировки.

---

## Top Performance Issues

### 1. LINQ в Hot Paths
**Modules:** Input, UIToolkit, StatAndAbilities

```csharp
// Каждый кадр:
var keys = _keyBindings.Select(x => x.Key).ToList();
```

**Solution:** Кэшировать или использовать for-loop.

---

### 2. ConcurrentDictionary без необходимости
**Modules:** Modding, Network.Shared.LiteNetLib

```csharp
private readonly ConcurrentDictionary<string, ModContainer> _loadedMods = new();
// Но все операции в одном потоке!
```

**Solution:** Использовать `Dictionary<TKey, TValue>`.

---

### 3. List.Remove O(n²)
**Module:** Tween.Core

```csharp
foreach(var tween in _tweensToRemove)
{
    _aliveTweens.Remove(tween); // O(n) в цикле
}
```

**Solution:** Использовать `RemoveAtSwapBack` или `HashSet`.

---

## Recommendations Priority

### P0 — Critical (Fix Immediately)
1. Реализовать обработчики событий в Network.Client/Server
2. Убрать GetAwaiter().GetResult() в Modding
3. Исправить race condition в AssetManagement

### P1 — High (Fix Soon)
1. Добавить Debug.Assert для DI-полей
2. Убрать LINQ из hot paths
3. Заменить ConcurrentDictionary на Dictionary где не нужно
4. Добавить sandbox для Lua-модов

### P2 — Medium (Technical Debt)
1. Оптимизировать Tween.Remove O(n²)
2. Добавить ObjectPool для Network.Writer
3. Разделить IWindow на IWindow + IWindowInput
4. Улучшить Exception messages

---

## Legend

| Status | Meaning |
|--------|---------|
| ✅ Good | Минорные проблемы, модуль готов к использованию |
| ⚠️ Needs Work | Есть критичные или высокоприоритетные проблемы |
| ❌ Critical | Модуль неработоспособен |

---

## Related Documentation

- [Modules Overview](../README.md)
- [Architecture Overview](../../architecture/overview.md)
- [Performance Overview](../../performance/overview.md)

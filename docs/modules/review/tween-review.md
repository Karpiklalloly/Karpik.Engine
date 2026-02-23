# Code Review: Tween.Core

**Module:** Tween.Core  
**Type:** Animation System  
**Status:** ✅ Good - Well-designed tweening library

---

## Overview

Система анимаций (tweening) для плавных переходов. Поддерживает последовательности, группы, easing functions, и интеграцию с ECS.

---

## Statistics

| Metric | Value |
|--------|-------|
| Files | 25+ |
| Interfaces | 3 |
| Classes | 20+ |
| Lines of Code | ~2000 |

---

## Issues Found

### HI-1: List.Remove в Tick() — O(n²) [High]

**File:** [`GTweensContext.cs`](Modules/Shared/Tween/Tween.Core/Tween/Contexts/GTweensContext.cs:80-86)

```csharp
foreach(GTween tween in _tweensToRemove)
{
    tween.IsAlive = false;
    _aliveTweens.Remove(tween);  // O(n) поиск + O(n) сдвиг
    _tweensToAdd.Remove(tween);  // Ещё один O(n)
}
```

**Problem:** `List.Remove()` — O(n) операция. В цикле по _tweensToRemove получается O(n²).

**Solution:** Использовать mark-and-sweep или HashSet:
```csharp
// Вариант 1: Mark-and-sweep (swap with last)
for (int i = _aliveTweens.Count - 1; i >= 0; i--)
{
    if (!_aliveTweens[i].IsPlaying)
    {
        _aliveTweens[i].IsAlive = false;
        _aliveTweens.RemoveAtSwapBack(i); // O(1)
    }
}

// Вариант 2: HashSet для быстрого удаления
private readonly HashSet<GTween> _aliveTweens = new();
```

---

### HI-2: Stopwatch каждый кадр — overhead [High]

**File:** [`GTweensContext.cs`](Modules/Shared/Tween/Tween.Core/Tween/Contexts/GTweensContext.cs:25)

```csharp
readonly Stopwatch _updateStopwatch = new();

public void Tick(float deltaTime)
{
    _updateStopwatch.Restart();
    // ...
    _updateStopwatch.Stop();
    TickDurationMs = _updateStopwatch.ElapsedMilliseconds;
}
```

**Problem:** Stopwatch.Restart/Stop каждый кадр создаёт overhead. В 60 FPS — 60 вызовов в секунду.

**Solution:** Использовать только в DEBUG или сделать опциональным:
```csharp
#if DEBUG || DEVELOPMENT
    _updateStopwatch.Restart();
#endif
// ... tick logic ...
#if DEBUG || DEVELOPMENT
    _updateStopwatch.Stop();
    TickDurationMs = _updateStopwatch.ElapsedMilliseconds;
#endif
```

---

### HI-3: DI поля без null! [High]

**File:** [`TweenUpdateSystem.cs`](Modules/Shared/Tween/Tween.Core/ECS/Systems/TweenUpdateSystem.cs:9)

```csharp
[DI] private Tween _tween = null!;
[DI] private Time _time = null!;
```

**Good:** Правильное использование `= null!` с NRT.

---

### MI-1: GTweenSequenceBuilder — mutable struct semantics [Medium]

**File:** [`GTweenSequenceBuilder.cs`](Modules/Shared/Tween/Tween.Core/Tween/Builders/GTweenSequenceBuilder.cs:10)

```csharp
public struct BuilderPart
{
    private string _name;
    private float _duration;
    private int _order;
    private Buff[]? _buffs;
    
    public BuilderPart WithName(string name)
    {
        _name = name;
        return this; // Возвращает копию!
    }
}
```

**Problem:** BuilderPart — struct. Методы With* возвращают копию, а не мутируют оригинал. Это может запутать.

**Solution:** Либо сделать class, либо документировать:
```csharp
// Вариант 1: class вместо struct
public class BuilderPart { ... }

// Вариант 2: Явно документировать
/// <summary>
/// Immutable builder. Each With* method returns a new instance.
/// </summary>
public readonly struct BuilderPart { ... }
```

---

### MI-2: Easing allocations [Medium]

**File:** [`PresetEasingDelegateFactory.cs`](Modules/Shared/Tween/Tween.Core/Tween/Easings/PresetEasingDelegateFactory.cs)

```csharp
// Фабрика создаёт новые delegate при каждом вызове?
```

**Problem:** Если EasingDelegate создаётся через lambda — это аллокация closure.

**Solution:** Использовать static методы или кэшировать:
```csharp
private static readonly Dictionary<EasingType, EasingDelegate> _cache = new();

public static EasingDelegate Get(EasingType type)
{
    return _cache.GetOrAdd(type, CreateEasing);
}
```

---

### MI-3: TweenUpdatePausableSystem — дублирование логики [Medium]

**File:** [`TweenUpdateSystem.cs`](Modules/Shared/Tween/Tween.Core/ECS/Systems/TweenUpdateSystem.cs:18-30)

```csharp
public class TweenUpdateSystem : IEcsRun
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void Run()
    {
        _tween.Update(_time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : IEcsRun
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void Run()
    {
        if (!_time.IsPaused)
        {
            _tween.UpdatePausable(_time.DeltaTime);
        }
    }
}
```

**Problem:** Дублирование DI-полей. Можно объединить или использовать наследование.

**Solution:**
```csharp
public abstract class BaseTweenSystem : IEcsRun
{
    [DI] protected Tween _tween = null!;
    [DI] protected Time _time = null!;
}
```

---

## Positive Aspects ✅

1. **Builder pattern** — удобный fluent API для создания tweens
2. **Easing functions** — богатый набор предустановок
3. **Sequence & Group** — поддержка сложных анимаций
4. **ECS integration** — системы для автоматического обновления
5. **Interpolators** — расширяемая система интерполяции
6. **Correct null! usage** — правильная работа с NRT

---

## Performance Analysis

| Operation | Complexity | Allocation |
|-----------|------------|------------|
| Play | O(1) | 0 (если переиспользовать) |
| Tick | O(n) | 0 |
| Remove | O(n) ⚠️ | 0 |

---

## Recommendations

| Priority | Action |
|----------|--------|
| **High** | Оптимизировать Remove в Tick (O(n²) → O(n)) |
| **High** | Сделать Stopwatch опциональным/DEBUG-only |
| Medium | Рассмотреть class вместо struct для Builder |
| Medium | Кэшировать Easing delegates |
| Medium | Убрать дублирование в TweenUpdateSystem |

---

## Verdict

**Хороший tweening-движок.** Удобный API, правильная архитектура. Основная проблема — O(n²) при удалении tweens. В остальном — качественная реализация.

# Code Review: StatAndAbilities

**Module:** StatAndAbilities  
**Type:** Game Mechanics (Stats, Buffs, Effects)  
**Status:** ✅ Good - Performance-oriented design

---

## Overview

Система статов и эффектов для RPG-механик. Реализована на базе DragonECS с использованием struct-based pool для zero-allocation.

---

## Statistics

| Metric | Value |
|--------|-------|
| Files | 9 |
| Structs | 5 |
| Classes | 3 |
| Lines of Code | ~400 |

---

## Issues Found

### HI-1: LINQ в EffectBuilder.Build() [High]

**File:** [`EffectBuilder.cs`](Modules/Shared/StatAndAbilities/Core/EffectBuilder.cs:63-71)

```csharp
public Effect BuildUnsafe() =>
    new()
    {
        Buffs = _buffs!.ToArray(), // LINQ ToArray() - аллокация
        Name = _name,
        Order = _order,
        Duration = _duration,
        IsPermanent = _duration.Equals(-1)
    };
```

**Problem:** `ToArray()` создаёт новую копию массива при каждом Build(). Если эффекты создаются часто — GC pressure.

**Solution:** Использовать исходный массив или Span:
```csharp
// Вариант 1: Не копировать если владение передаётся
Buffs = _buffs ?? Array.Empty<Buff>()

// Вариант 2: Использовать pooled array
Buffs = ArrayPool<Buff>.Shared.RentAndCopy(_buffs)
```

---

### HI-2: Singleton StatPool — проблема для тестирования [High]

**File:** [`StatPool.cs`](Modules/Shared/StatAndAbilities/Core/StatPool.cs:10)

```csharp
public class StatPool<T> where T : struct, IStat
{
    public static StatPool<T> Instance { get; } = new();
}
```

**Problem:** Singleton затрудняет тестирование. Нельзя создать изолированный pool для тестов.

**Solution:** Инжектировать через DI или использовать контекст:
```csharp
// Вариант 1: DI
services.AddSingleton(typeof(StatPool<>));

// Вариант 2: Контекст (как в DragonECS)
public class StatWorld
{
    private readonly Dictionary<Type, object> _pools = new();
    public StatPool<T> GetPool<T>() where T : struct, IStat => ...;
}
```

---

### HI-3: float.Equals(-1) вместо == [High]

**File:** [`EffectBuilder.cs`](Modules/Shared/StatAndAbilities/Core/EffectBuilder.cs:70)

```csharp
IsPermanent = _duration.Equals(-1)
```

**Problem:** `float.Equals()` проверяет точное равенство битов. Для float лучше использовать сравнение с epsilon или явную константу.

**Solution:**
```csharp
private const float PERMANENT_DURATION = -1f;

IsPermanent = Math.Abs(_duration - PERMANENT_DURATION) < float.Epsilon;
// Или просто
IsPermanent = _duration < 0f; // Любое отрицательное = permanent
```

---

### MI-1: ValidateMapping удваивает размер, но не проверяет достаточно [Medium]

**File:** [`StatPool.cs`](Modules/Shared/StatAndAbilities/Core/StatPool.cs:165-171)

```csharp
private void ValidateMapping(int entityID)
{
    if (_mapping.Length <= entityID)
    {
        Array.Resize(ref _mapping, _mapping.Length << 1);
    }
}
```

**Problem:** Если entityID очень большой (например, после удаления множества entity), массив будет расти экспоненциально, но может не хватить одного удвоения.

**Solution:**
```csharp
private void ValidateMapping(int entityID)
{
    if (_mapping.Length <= entityID)
    {
        int newSize = Math.Max(_mapping.Length << 1, entityID + 1);
        Array.Resize(ref _mapping, newSize);
    }
}
```

---

### MI-2: Exception messages без контекста [Medium]

**File:** [`StatPool.cs`](Modules/Shared/StatAndAbilities/Core/StatPool.cs:31-35)

```csharp
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
if (itemIndex > 0)
{
    throw new Exception(entityID.ToString());
}
#endif
```

**Problem:** Exception message содержит только entityID, без объяснения что произошло.

**Solution:**
```csharp
throw new InvalidOperationException($"Stat of type {typeof(T).Name} already exists for entity {entityID}");
```

---

### MI-3: ClearAll неэффективен [Medium]

**File:** [`StatPool.cs`](Modules/Shared/StatAndAbilities/Core/StatPool.cs:139-147)

```csharp
public void ClearAll()
{
    _recycledItemsCount = 0;
    if (_itemsCount <= 0) { return; }
    for (int i = 0; i < _mapping.Length; i++)
    {
        TryDel(i); // TryDel вызывает Has() для каждого i
    }
}
```

**Problem:** O(n) с лишними проверками Has() для каждого индекса.

**Solution:**
```csharp
public void ClearAll()
{
    _recycledItemsCount = 0;
    for (int i = 1; i <= _itemsCount; i++)
    {
        DisableStat(ref _items[i]);
    }
    Array.Clear(_mapping, 0, _mapping.Length);
    _itemsCount = 0;
}
```

---

## Positive Aspects ✅

1. **Struct-based pool** — zero allocation для статов
2. **ref returns** — прямой доступ к данным без копирования
3. **AggressiveInlining** — правильное использование атрибутов
4. **Recycled items** — переиспользование слотов
5. **IStat interface** — гибкость для разных типов статов

---

## Performance Analysis

| Operation | Complexity | Allocation |
|-----------|------------|------------|
| Add | O(1) amortized | 0 |
| Get | O(1) | 0 |
| Del | O(1) | 0 |
| Has | O(1) | 0 |
| ClearAll | O(n) | 0 |

---

## Recommendations

| Priority | Action |
|----------|--------|
| **High** | Убрать LINQ ToArray() в Build() |
| **High** | Заменить Singleton на DI |
| **High** | Исправить float comparison |
| Medium | Оптимизировать ClearAll |
| Medium | Улучшить Exception messages |
| Medium | Исправить ValidateMapping для больших ID |

---

## Verdict

**Отличный модль с точки зрения производительности.** Struct-based pool с ref returns — правильный подход для game mechanics. Основные проблемы — Singleton (тестируемость) и LINQ в builder'е.

# StatAndAbilities

> Система статов и эффектов

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: — (без инсталлера, библиотека)

## 🎯 Назначение

Предоставляет систему для работы со статами (характеристиками) и эффектами (баффами/дебаффами).

## 📦 Сервисы

Нет сервисов. Используется напрямую через `StatContainer`.

## 🔧 ECS-системы

Нет ECS-систем. Интегрируется через компоненты.

## 📁 Структура

```
StatAndAbilities/
├── Core/
│   ├── IStat.cs              # Интерфейс стата
│   ├── StatContainer.cs      # Контейнер статов
│   ├── StatPool.cs           # Пул статов (generic)
│   ├── StatAttribute.cs      # Атрибут стата
│   ├── Buff.cs               # Бафф
│   ├── BuffType.cs           # Тип баффа
│   ├── BuffRange.cs          # Диапазон баффа
│   ├── Effect.cs             # Эффект
│   ├── EffectBuilder.cs      # Билдер эффектов
│   └── Default.cs            # Дефолтные значения
```

## 🔗 Зависимости

Нет внешних зависимостей.

## 💡 Использование

```csharp
// Определение стата
public struct Health : IStat
{
    public float Value;
    public float MaxValue;
}

// Использование
var stats = new StatContainer(entityId);
ref var health = ref stats.Add<Health>();
health.Value = 100;
health.MaxValue = 100;

// Проверка наличия
if (stats.Has<Health, Mana>())
{
    ref var h = ref stats.Get<Health>();
}

// Эффекты
var effect = EffectBuilder.Create()
    .AddBuff(new Buff { Type = BuffType.Add, Value = 10 })
    .Build();
```

## ⚠️ Особенности

- **Zero-allocation**: `StatPool<T>` использует `Dictionary<int, T>` с `ref` возвратом
- **Type-safe**: Каждый стат — отдельный тип
- **AggressiveInlining**: Методы инлайнятся для производительности
- Поддержка множественных баффов на один стат

## 🚨 Performance Notes

| Аспект | Решение |
|--------|---------|
| `ref` возврат | Избегает копирования структур |
| `StatPool<T>` | Статический пул на тип |
| `AggressiveInlining` | Максимальная производительность |

# Tween

> Система анимаций и интерполяций

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: — (интегрируется через ECS)

## 🎯 Назначение

Предоставляет систему для создания плавных анимаций (tweening) с поддержкой различных типов данных и easing-функций.

## 📦 Сервисы

Нет сервисов. Используется через статические методы и ECS.

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| `TweenUpdateSystem` | POST_END_LAYER | Обновление твингов |
| `TweenUpdatePausableSystem` | POST_END_LAYER | Обновление с паузой |

## 📁 Структура

```
Tween.Core/
├── Tween.cs                  # Основной класс
├── ECS/
│   ├── TweenModule.cs        # ECS-модуль
│   └── Systems/
│       ├── TweenUpdateSystem.cs
│       └── TweenUpdatePausableSystem.cs
└── Tween/
    ├── Builders/
    │   └── GTweenSequenceBuilder.cs  # Билдер последовательностей
    ├── Contexts/
    │   └── GTweensContext.cs         # Контекст твингов
    ├── Delegates/
    │   └── ValidationDelegates.cs    # Делегаты валидации
    ├── Easings/
    │   ├── Easing.cs                 # Базовый easing
    │   ├── EasingDelegate.cs         # Делегат easing
    │   └── PresetEasingDelegateFactory.cs  # Пресеты
    ├── Enums/
    │   ├── ResetMode.cs              # Режим сброса
    │   └── RotationMode.cs           # Режим вращения
    ├── Extensions/
    │   ├── GTweenExtensions.cs       # Расширения API
    │   ├── AngleExtensions.cs        # Работа с углами
    │   ├── MathExtensions.cs         # Математика
    │   ├── SystemColorExtensions.cs  # Цвета
    │   └── ValidationExtensions.cs   # Валидация
    ├── Interpolators/
    │   ├── IInterpolator.cs          # Интерфейс интерполятора
    │   ├── FloatInterpolator.cs
    │   ├── IntInterpolator.cs
    │   ├── SystemColorInterpolator.cs
    │   ├── SystemQuaternionInterpolator.cs
    │   ├── SystemVector2Interpolator.cs
    │   ├── SystemVector3Interpolator.cs
    │   ├── SystemVector3RotationInterpolator.cs
    │   └── SystemVector4Interpolator.cs
    ├── TweenBehaviours/
    │   ├── ITweenBehaviour.cs        # Интерфейс поведения
    │   ├── TweenBehaviour.cs         # Базовое поведение
    │   ├── CallbackTweenBehaviour.cs # Колбэки
    │   ├── GroupTweenBehaviour.cs    # Группы
    │   ├── InterpolationTweenBehaviour.cs  # Интерполяция
    │   ├── SequenceTweenBehaviour.cs # Последовательности
    │   └── WaitTimeTween.cs          # Ожидание
    └── Tweeners/
        ├── ITweener.cs               # Интерфейс твинера
        ├── FloatTweener.cs
        ├── IntTweener.cs
        ├── SystemColorTweener.cs
        ├── SystemQuaternionTweener.cs
        ├── SystemVector2Tweener.cs
        ├── SystemVector3Tweener.cs
        └── SystemVector4Tweener.cs
```

## 🔗 Зависимости

Нет внешних зависимостей.

## 💡 Использование

```csharp
// Простой твиг
Tween.To(
    () => transform.Position,
    v => transform.Position = v,
    targetPosition,
    duration
).SetEasing(Easing.OutQuad);

// Последовательность
var sequence = Tween.Sequence()
    .Append(Tween.To(...))
    .AppendCallback(() => Debug.Log("Done"))
    .AppendInterval(0.5f)
    .Append(Tween.To(...));
```

## ⚠️ Особенности

- Поддержка типов: float, int, Vector2, Vector3, Vector4, Quaternion, Color
- Множество easing-функций (In/Out/InOut для Quad, Cubic, Elastic, etc.)
- Последовательности и параллельные группы
- Колбэки на события (OnStart, OnComplete, OnUpdate)
- Паузируемые твинги

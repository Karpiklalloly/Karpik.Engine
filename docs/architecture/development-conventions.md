# Development Conventions

> Дата создания: 2026-03-26

## Test-Driven Development (TDD)

### Принцип: Tests First

1. **Сначала тест** — пишем тест до реализации
2. **Богатые тесты** — не для галочки, а для полного покрытия сценариев
3. **Red-Green-Refactor** — красный тест → зеленый → рефакторинг

### Структура тестов

```csharp
public class CalculatorTests
{
    // Группировка по функциональности
    public class Add
    {
        [Fact]
        public void TwoPositiveNumbers_ReturnsSum()
        {
            // Arrange
            var calculator = new Calculator();
            
            // Act
            var result = calculator.Add(2, 3);
            
            // Assert
            Assert.Equal(5, result);
        }
        
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(-1, 1, 0)]
        [InlineData(100, 200, 300)]
        public void VariousInputs_ReturnsCorrectSum(int a, int b, int expected)
        {
            var calculator = new Calculator();
            Assert.Equal(expected, calculator.Add(a, b));
        }
        
        [Fact]
        public void Overflow_ThrowsException()
        {
            var calculator = new Calculator();
            Assert.Throws<OverflowException>(() => 
                calculator.Add(int.MaxValue, 1));
        }
    }
    
    public class Subtract { /* ... */ }
    public class Multiply { /* ... */ }
}
```

### Что делает тесты богатыми

| Аспект | Пример |
|--------|--------|
| **Positive cases** | `2 + 3 = 5` |
| **Negative cases** | `-1 + 1 = 0` |
| **Edge cases** | `0 + 0`, `int.MaxValue + 1` |
| **Boundary cases** | Границы диапазонов |
| **Error cases** | `null` аргументы, переполнение |
| **State transitions** | Изменение состояния объекта |

### Тесты компонентов ECS

```csharp
public class PositionComponentTests
{
    [Fact]
    public void Create_SetsDefaultValues()
    {
        var component = new Position();
        
        Assert.Equal(0f, component.X);
        Assert.Equal(0f, component.Y);
    }
    
    [Fact]
    public void SetPosition_UpdatesValues()
    {
        var component = new Position();
        
        component.X = 10f;
        component.Y = 20f;
        
        Assert.Equal(10f, component.X);
        Assert.Equal(20f, component.Y);
    }
    
    [Fact]
    public void Implements_IEcsComponent_HasCorrectTypeId()
    {
        var component = new Position();
        Assert.IsAssignableFrom<IEcsComponent>(component);
    }
}
```

### Запуск тестов

```bash
# Все тесты проекта
dotnet test

# Конкретный проект
dotnet test Modules/Shared/ECS/ECS.Core.Tests/ECS.Core.Tests.csproj

# С фильтром
dotnet test --filter "FullyQualifiedName~Position"
```

---

## Файловая организация

### Правило: One Type Per File

Каждый класс, структура, интерфейс — **в отдельном файле**.

```csharp
// Правильно:
Modules/Shared/ECS/ECS.Core/Src/
├── EcsWorld.cs
├── EcsPool.cs
├── EcsDefaultWorld.cs
├── EcsAspect.cs
└── IEcsComponent.cs

// Неправильно:
Modules/Shared/ECS/ECS.Core/Src/
├── EcsWorld.cs       // содержит EcsPool, EcsDefaultWorld
├── Helpers.cs        // содержит кучу вспомогательных классов
└── Utils.cs          // содержит всё подряд
```

### Исключение: ECS компоненты

В модулях (не в MyGame) компонентов мало, поэтому допускается несколько в одном файле:

```csharp
// Modules/Shared/ECS/ECS.Core/Src/Components.cs
public struct Position : IEcsComponent { public float X, Y; }
public struct Velocity : IEcsComponent { public float X, Y; }
public struct Health : IEcsComponent { public float Value; }
```

Но если компонентов > 5 — разделять по файлам.

### Структура проекта

```
Modules/Shared/ECS/ECS.Core/
├── ECS.Core.csproj
├── Src/
│   ├── EcsWorld.cs           # Главный класс
│   ├── EcsPool.cs            # Пул компонентов
│   ├── EcsDefaultWorld.cs    # Реализация по умолчанию
│   ├── EcsAspect.cs         # Фильтрация
│   ├── IEcsComponent.cs     # Интерфейс компонента
│   ├── Components.cs        # Исключение: несколько компонентов
│   └── ...
├── Tests/                    # Тесты рядом с кодом (не в отдельном проекте)
│   └── Src/
│       ├── EcsWorldTests.cs
│       └── EcsPoolTests.cs
└── README.md                 # module-level документация
```

### Тестовый проект

Для библиотек — отдельный тестовый проект:

```
Modules/Shared/ECS/ECS.Core.Tests/
├── ECS.Core.Tests.csproj
└── Src/
    ├── EcsWorldTests.cs
    ├── EcsPoolTests.cs
    └── EcsAspectTests.cs
```

### MyGame — исключение

В MyGame много файлов — допускается группировка по папкам:

```
MyGame/Client/MyGame.Client.Main/
├── Systems/          # Все системы в одной папке
│   ├── InputSystem.cs
│   ├── DrawSystem.cs
│   └── ...
├── Components.cs     # Здесь компоненты конкретной игры
├── MyGameClientInstaller.cs
└── ...
```

---

## Соглашения именования

### Файлы

| Тип | Пример |
|-----|--------|
| Класс/Структура | `EcsWorld.cs` |
| Интерфейс | `IRenderer.cs` |
| Тесты | `EcsWorldTests.cs` |
| Enum | `UiTypeId.cs` (или Enums.cs если их несколько) |

### Классы и структуры

```csharp
public class EcsWorld { }           // PascalCase
public struct Vector2 { }            // PascalCase
public interface IRenderer { }        // I prefix для интерфейсов
public abstract class BaseModule { } // Base prefix для базовых классов
```

### Тесты

```csharp
public class EcsWorldTests { }              // ClassNameTests
public class WhenAddingEntity               // Given-When-Then
{
    public class AndWidgetIsVisible { }    // Nested для группировки
}
```

---

## Code Style

### using директивы

```csharp
using System;
using System.Collections.Generic;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;

// Группировка: System → External → Project
// Без пустых строк между группами
```

### Модификаторы доступа

```csharp
public class EcsWorld { }    // Явно public
internal class Helper { }    // Явно internal для внутренних
    
// private/protected только если действительно нужно
```

### Поля

```csharp
public class EcsWorld
{
    private EcsPool[] _pools;          // _camelCase для private
    private readonly int _capacity;    // readonly явно
    
    public int Count { get; }           // Properties вместо public полей
}
```

---

## Documentation

### Класс/Модуль

```csharp
/// <summary>
/// Core ECS world that manages entities and components.
/// </summary>
/// <remarks>
/// Thread-safe implementation for server-side usage.
/// </remarks>
public class EcsWorld { }
```

### Публичный API

```csharp
/// <summary>
/// Creates a new entity and returns its ID.
/// </summary>
/// <returns>Unique entity identifier.</returns>
public entlong NewEntity();
```

---

## Проверка конвенций

### Сборка и тесты

```bash
# Сборка всего
dotnet build

# Тесты
dotnet test

# Тесты + покрытие
dotnet test --collect:XPlatCodeCoverage
```

### Анализ кода

```bash
# dotnet format
dotnet format --verify-no-changes
```
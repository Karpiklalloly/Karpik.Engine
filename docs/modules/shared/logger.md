# Logger

> Система логирования

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`

## 🎯 Назначение

Предоставляет унифицированную систему логирования с цветным выводом в консоль.

## 📦 Сервисы

| Интерфейс | Реализация | Описание |
|-----------|------------|----------|
| `ILoggerFactory` | `LoggerFactory` | Engine-scoped factory |
| `ILogger<T>` | Microsoft `Logger<T>` | Typed logger |
| `ILoggerFactoryModifier` | Game-provided | Optional factory configuration |

## 🔧 ECS-системы

Нет ECS-систем.

## 📁 Структура

```
LoggerModule/
├── LoggerModuleInstaller.cs          # Регистрация ILoggerFactory
├── ILoggerFactoryModifier.cs         # Настройка провайдеров игрой
├── EditorConsoleLoggerExtensions.cs  # Передача логов в редактор
└── ConsoleChangers.cs                # Цветной вывод в консоль
```

## 🔗 Зависимости

Нет внешних зависимостей.

## 💡 Настройка провайдеров

```csharp
[Export(typeof(ILoggerFactoryModifier))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class GameLoggingModifier : ILoggerFactoryModifier
{
    public void Modify(ILoggingBuilder builder)
    {
        builder.AddProvider(new GameLoggerProvider());
    }
}
```

The logger module adds the simple console provider, optional editor-capture
provider, and Trace minimum level before invoking exported modifiers once during
factory creation. A modifier can add providers or deliberately call
`ClearProviders()` to replace the defaults. The factory owns and disposes its
configured providers; modifiers run during engine construction, outside gameplay
and ECS hot paths.

## ⚠️ Особенности

- Simple console output is enabled by default.
- Editor capture is enabled when `KARPIK_EDITOR_LOG_CAPTURE=1`.
- The factory disposes providers configured by its modifiers.

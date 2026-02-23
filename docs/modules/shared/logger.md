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
| `ILogger` | `Logger` | Синглтон логгера |

## 🔧 ECS-системы

Нет ECS-систем.

## 📁 Структура

```
LoggerModule/
├── LoggerInstaller.cs   # Инсталлер модуля
├── Logger.cs            # Реализация логгера
└── ConsoleChangers.cs   # Цветной вывод в консоль
```

## 🔗 Зависимости

Нет внешних зависимостей.

## 💡 Использование

```csharp
var logger = services.Get<ILogger>();

logger.Info("Game started");
logger.Warning("Low memory");
logger.Error("Failed to load asset");
```

## ⚠️ Особенности

- Singleton паттерн
- Цветной вывод в консоль
- Уровни логирования: Info, Warning, Error

# Network.Server

> Сетевой сервер на базе LiteNetLib

## 📋 Обзор

- **Слой**: Server
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`

## 🎯 Назначение

Обеспечивает серверную часть сетевого взаимодействия, обработку подключений и синхронизацию клиентов.

## 📦 Сервисы

Сервисы регистрируются в `Network.Shared` модуле.

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| `UpdateSystem` | Update | Обработка сетевых событий сервера |

## 📁 Структура

```
Network.Server.LiteNetLib/
├── NetworkServerInstaller.cs  # Инсталлер модуля
├── NetworkServerModule.cs     # ECS-модуль
└── Systems/
    └── UpdateSystem.cs        # Обработка событий
```

## 🔗 Зависимости

- `Network.Shared.Core` — абстракции сети
- `Network.Shared.LiteNetLib` — реализация LiteNetLib
- `LiteNetLib` — библиотека UDP

## 💡 Использование

```csharp
// Сервер автоматически запускается при инициализации
// Обработка подключений через INetworkManager
var networkManager = services.Get<INetworkManager>();
```

## ⚠️ Особенности

- Поддержка множественных клиентов
- Reliable и Unreliable доставка
- RPC для удалённого вызова методов
- Синхронизация компонентов

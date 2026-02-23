# Network.Client

> Сетевой клиент на базе LiteNetLib

## 📋 Обзор

- **Слой**: Client
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`

## 🎯 Назначение

Обеспечивает сетевое соединение клиента с сервером, обработку пакетов и RPC.

## 📦 Сервисы

Сервисы регистрируются в `Network.Shared` модуле.

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| `InitNetworkClientSystem` | Init | Инициализация клиента |
| `UpdateNetworkClientSystem` | Update | Обработка сетевых событий |

## 📁 Структура

```
Network.Client.LiteNetLib/
├── NetworkClientInstaller.cs       # Инсталлер модуля
├── NetworkClientModule.cs          # ECS-модуль
└── Systems/
    ├── InitNetworkClientSystem.cs  # Инициализация
    ├── UpdateNetworkClientSystem.cs # Обновление
    └── NetworkConfig.cs            # Конфигурация
```

## 🔗 Зависимости

- `Network.Shared.Core` — абстракции сети
- `Network.Shared.LiteNetLib` — реализация LiteNetLib
- `LiteNetLib` — библиотека UDP

## 💡 Использование

```csharp
// Конфигурация через NetworkConfig
var config = new NetworkConfig
{
    ServerAddress = "127.0.0.1",
    ServerPort = 9050
};
```

## ⚠️ Особенности

- Использует LiteNetLib для UDP соединения
- Автоматическое переподключение
- Поддержка Reliable и Unreliable доставки

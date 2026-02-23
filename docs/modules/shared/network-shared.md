# Network.Shared

> Общие сетевые абстракции

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`

## 🎯 Назначение

Предоставляет интерфейсы и общую логику для сетевого взаимодействия.

## 📦 Сервисы

| Интерфейс | Реализация (LiteNetLib) | Описание |
|-----------|-------------------------|----------|
| `INetworkManager` | `LiteNetLibNetworkManager` | Менеджер сети |
| `IPeer` | `LiteNetLibPeer` | Удалённый пир |
| `IReader` | `LiteNetLibReader` | Чтение пакетов |
| `IWriter` | `LiteNetLibWriter` | Запись пакетов |

## 🔧 ECS-системы

Системы находятся в клиентском и серверном модулях.

## 📁 Структура

```
Network.Shared.Core/
├── INetworkManager.cs        # Менеджер сети
├── IPeer.cs                  # Пир соединения
├── IReader.cs                # Чтение данных
├── IWriter.cs                # Запись данных
├── IRpc.cs                   # RPC интерфейс
├── IConnectionRequest.cs     # Запрос на подключение
├── IDisconnectInfo.cs        # Информация о дисконнекте
├── ITargetRpcSender.cs       # Отправка RPC
├── ConnectionState.cs        # Состояние соединения
├── DeliveryMethod.cs         # Методы доставки
├── PacketType.cs             # Типы пакетов
├── ComponentAttribute.cs     # Атрибут для сетевых компонентов
├── Components.cs             # Сетевые компоненты
└── SerializerExtensions.cs   # Расширения для сериализации

Network.Shared.LiteNetLib/
├── LiteNetLibNetworkInstaller.cs  # Инсталлер
├── LiteNetLibNetworkManager.cs    # Реализация менеджера
├── LiteNetLibPeer.cs              # Реализация пира
├── LiteNetLibReader.cs            # Реализация ридера
├── LiteNetLibWriter.cs            # Реализация райтера
├── LiteNetLibConnectionRequest.cs # Запрос подключения
└── LiteNetLibDisconnectInfo.cs    # Информация о дисконнекте
```

## 🔗 Зависимости

- `LiteNetLib` — UDP библиотека

## 💡 Использование

```csharp
// Отправка данных
var writer = networkManager.CreateWriter();
writer.Put("Hello");
peer.Send(writer, DeliveryMethod.Reliable);

// Получение данных
public void OnDataReceived(IReader reader)
{
    var message = reader.GetString();
}
```

## ⚠️ Особенности

- Абстракция над сетевой библиотекой
- Поддержка Reliable и Unreliable доставки
- RPC для удалённого вызова методов
- Автоматическая сериализация компонентов

# Modules

> Модульная архитектура KarpikEngine

## 📁 Структура

```
Modules/
├── Client/           # Клиентские модули
├── Server/           # Серверные модули
└── Shared/           # Общие модули (Client + Server)
```

## 🎯 Назначение

Модули реализуют интерфейс `IModule` и обеспечивают:
- Регистрацию сервисов в DI-контейнере
- Конфигурацию ECS-систем
- Hot Reload поддержку
- Межмодульное взаимодействие

## 📑 Клиентские модули

| Модуль | Описание | Документация |
|--------|----------|--------------|
| Graphics.Raylib | Рендеринг через Raylib | [graphics-raylib.md](client/graphics-raylib.md) |
| Input | Обработка ввода | [input.md](client/input.md) |
| Network.Client | Сетевой клиент | [network-client.md](client/network-client.md) |
| UIToolkit | UI-фреймворк | [uitoolkit.md](client/uitoolkit.md) |

## 📑 Серверные модули

| Модуль | Описание | Документация |
|--------|----------|--------------|
| Network.Server | Сетевой сервер | [network-server.md](server/network-server.md) |

## 📑 Общие модули

| Модуль | Описание | Документация |
|--------|----------|--------------|
| AssetManagement | Загрузка ассетов | [asset-management.md](shared/asset-management.md) |
| ECS | Интеграция DragonECS | [ecs.md](shared/ecs.md) |
| Logger | Логирование | [logger.md](shared/logger.md) |
| Modding | Система модов | [modding.md](shared/modding.md) |
| Network.Shared | Общая сеть | [network-shared.md](shared/network-shared.md) |
| StatAndAbilities | Статы и эффекты | [stat-and-abilities.md](shared/stat-and-abilities.md) |
| Tween | Анимации | [tween.md](shared/tween.md) |

## 🔄 Жизненный цикл модуля

```
1. OnRegisterServices()  → Регистрация сервисов
2. OnConfigure()         → Конфигурация ECS
3. OnConfigureComplete() → Финализация
4. [Runtime]             → Работа
5. Destroy()             → Очистка (опционально)
```

## 🔗 Зависимости

Модули загружаются в порядке приоритета (атрибут `[Module(priority)]`):
- `-100` — AssetManagement (загружается первым)
- `0` — Стандартный приоритет (по умолчанию)
- `1+` — Загружаются после стандартных

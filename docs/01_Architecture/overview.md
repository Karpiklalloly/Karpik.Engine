# Architecture Overview

> 📅 Обновлено: 2026-03-26

## 🏗️ Структура проекта

```
KarpikEngine/
├── Karpik.Jobs/              # Job System (свой)
├── Karpik.Engine.Core/       # Ядро движка
│   ├── Bootstrap.cs          # Инициализация
│   ├── ProcessManagement/    # IPC + Process Isolation
│   └── Services/             # DI
├── Dragon/                   # DragonECS (сторонний)
├── Modules/
│   ├── Client/               # Клиентские модули
│   │   ├── Graphics/         # Raylib
│   │   ├── Network.Client/   # LiteNetLib
│   │   ├── UIToolkit/
│   │   └── UI/GameUI/
│   ├── Server/               # Серверные модули
│   │   └── Network.Server/
│   └── Shared/               # Общие модули
│       ├── ECS/              # ECS интеграция
│       ├── AssetManagement/
│       ├── Network.Shared/
│       └── Physics/
├── MyGame/                   # Пример игры
│   ├── Client/
│   ├── Server/
│   └── Shared/
└── docs/                     # Документация
```

---

## 📐 Принципы разработки

### TDD (Test-Driven Development)

- **Сначала тесты** — пишем тест до реализации
- **Богатые тесты** — полное покрытие сценариев
- **Red-Green-Refactor** — красный → зеленый → рефакторинг

Подробнее: [Development Conventions](development-conventions.md)

### Файловая организация

- **One Type Per File** — каждый класс/структура в отдельном файле
- **Исключение** — ECS компоненты в модулях (если < 5)
- **Тесты рядом** — либо в той же директории, либо в `{Module}.Tests` проекте

---

## 🔄 Process Isolation

```
┌─────────────────────────────────────┐
│  ClientLauncher (Watcher)           │
│  - ProcessManager                   │
│  - IPC Server                       │
│  - FileSystemWatcher                │
└──────────────┬──────────────────────┘
               │ Named Pipe
               ▼
┌─────────────────────────────────────┐
│  Karpik.Engine.Core.Runner (Worker) │
│  - Bootstrap                        │
│  - Modules (Raylib, ImGui)          │
│  - IPC Client                       │
└─────────────────────────────────────┘
```

**Преимущества:**
- Native DLL unloading (Raylib, ImGui)
- Crash isolation
- Hot reload без memory leaks

**Подробнее:** [Process Isolation Architecture](../../plans/process-isolation-architecture.md)

---

## 📦 Внешние зависимости

| Пакет | Назначение | Тип |
|-------|------------|-----|
| DragonECS | ECS фреймворк | Сторонний |
| Raylib | Графика | Native |
| LiteNetLib | Сеть | Managed |
| MoonSharp | Lua моддинг | Managed |

---

## 🔗 Связанные документы

- [Roadmap](../roadmap.md)
- [Performance Overview](../performance/overview.md)

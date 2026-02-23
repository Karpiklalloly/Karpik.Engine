# Architecture Overview

> 📅 Обновлено: 2026-02-19

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
│   │   └── UIToolkit/
│   └── Shared/               # Общие модули
│       ├── ECS/              # ECS интеграция
│       ├── AssetManagement/
│       └── Network.Shared/
└── Generated/                # Codegen
```

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

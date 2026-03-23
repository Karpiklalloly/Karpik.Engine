# Roadmap

> 📅 Обновлено: 2026-03-23

## 📊 Статус

| Приоритет | Открыто | В работе | Закрыто |
|-----------|---------|----------|---------|
| 🔴 P0 | 10 | 0 | 0 |
| 🟡 P1 | 2 | 0 | 0 |
| 🟢 P2 | 3 | 0 | 0 |

---

## 🔴 P0 - Critical

> **Блокирует production** | GC pressure в каждом кадре | **Срочно: до релиза**

| # | Задача | Файл | Срочность | Статус |
|---|--------|------|-----------|--------|
| P0-1 | CTS pool в JobSystem | `Karpik.Jobs/JobSystem.cs:153` | До релиза | [ ] |
| P0-2 | ArrayPool в IPC | `IpcProtocol.cs:38` | До релиза | [ ] |
| P0-3 | Создать проект UI.Core с базовыми структурами (UiNode, PropsBlob, пулы) | `Modules/Client/UI/UI.Core/` | До релиза | [ ] |
| P0-4 | Создать проект UI.Immediate с immediate-mode виджетами (Button, Label, InputField) | `Modules/Client/UI/UI.Immediate/` | До релиза | [ ] |
| P0-5 | Реализовать парсер UXML с временными буферами и атомарным swap | `Modules/Client/UI/UI.Core/Src/UxmlParser.cs` | До релиза | [ ] |
| P0-6 | Реализовать парсер USS и применение правил стилей (StyleRule, StyleVar стек) | `Modules/Client/UI/UI.Core/Src/UssParser.cs` | До релиза | [ ] |
| P0-7 | Реализовать layout‑engine для контейнеров `<Horizontal>` и `<Vertical>` (поддержка px, %) | `Modules/Client/UI/UI.Core/Src/LayoutEngine.cs` | До релиза | [ ] |
| P0-8 | Реализовать render‑проход, использующий immediate‑mode API и стек стилей | `Modules/Client/UI/UI.Immediate/Src/Renderer.cs` | До релиза | [ ] |
| P0-9 | Реализовать систему биндингов (getter/setter делегаты) и обработчиков событий | `Modules/Client/UI/UI.Core/Src/Binding.cs` | До релиза | [ ] |
| P0-10 | Реализовать локализацию (таблица строк, атрибут `@Loc`) | `Modules/Client/UI/UI.Core/Src/Localization.cs` | До релиза | [ ] |
| P0-11 | Реализовать горячую перезагрузку UXML/USS через FileSystemWatcher и пулы | `Modules/Client/UI/UI.Core/Src/HotReloader.cs` | До релиза | [ ] |
| P0-12 | Реализовать долгоживущий массив состояний виджетов (ElementState[]) для фокуса, скролла, каретки | `Modules/Client/UI/UI.Core/Src/ElementState.cs` | До релиза | [ ] |
| P0-13 | Реализовать hit‑testing и обработку ввода (обратный проход, Z‑индекс, отсечение) | `Modules/Client/UI/UI.Immediate/Src/HitTesting.cs` | До релиза | [ ] |
| P0-14 | Реализовать world‑space UI (BeginWorldSpace/EndWorldSpace, интеграция с прозрачным passом) | `Modules/Client/UI/UI.Immediate/Src/WorldSpace.cs` | До релиза | [ ] |
| P0-15 | Обеспечить нулевые managed аллокации в кадре (тесты через GC.GetAllocatedBytes) | `Modules/Client/UI/UI.Core.Tests/` | До релиза | [ ] |
| P0-16 | Написать unit‑тесты иベンчмарки для layout/render производительности | `Modules/Client/UI/UI.Core.Tests/` и `Modules/Client/UI/UI.Immediate.Tests/` | До релиза | [ ] |

---

## 🟡 P1 - High

> **Влияет на масштабирование** | Contention при нагрузках | **Срочно: до масштабного проекта**

| # | Задача | Файл | Срочность | Статус |
|---|--------|------|-----------|--------|
| P1-1 | Work-stealing deque | `JobSystem.cs:57` | До масштабного проекта | [ ] |
| P1-2 | Span API для AssetHandle | `AssetHandle.cs` | Когда будет время | [ ] |

---

## 🟢 P2 - Medium

> **Улучшения** | Качество кода | **Срочно: когда будет время**

| # | Задача | Срочность | Статус |
|---|--------|-----------|--------|
| P2-1 | AggressiveInlining в AssetHandle | Когда будет время | [ ] |
| P2-2 | Кэш reflection в Bootstrap | Когда будет время | [ ] |
| P2-3 | Benchmarks для JobSystem | Когда будет время | [ ] |

---

## 🔗 Документация

- [Architecture Overview](docs/architecture/overview.md)
- [Performance Overview](docs/performance/overview.md)
- [Job System Details](docs/performance/job-system.md)
- [IPC Details](docs/performance/ipc.md)
- [Process Isolation](plans/process-isolation-architecture.md)

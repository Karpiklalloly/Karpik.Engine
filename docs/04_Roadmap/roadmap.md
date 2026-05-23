# Roadmap

> 📅 Обновлено: 2026-02-19

## 📊 Статус

| Приоритет | Открыто | В работе | Закрыто |
|-----------|---------|----------|---------|
| 🔴 P0 | 2 | 0 | 0 |
| 🟡 P1 | 2 | 0 | 0 |
| 🟢 P2 | 3 | 0 | 0 |

---

## 🔴 P0 - Critical

> **Блокирует production** | GC pressure в каждом кадре | **Срочно: до релиза**

| # | Задача | Файл | Срочность | Статус |
|---|--------|------|-----------|--------|
| P0-1 | CTS pool в JobSystem | `Karpik.Jobs/JobSystem.cs:153` | До релиза | [ ] |
| P0-2 | ArrayPool в IPC | `IpcProtocol.cs:38` | До релиза | [ ] |

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

## 🔗 Связанные документы

- [Backlog](backlog.md)
- [Architecture Overview](../01_Architecture/overview.md)
- [Job System Research](../03_Research/job-system.md)
- [Process Isolation](../../plans/process-isolation-architecture.md)
- [Улучшение сети](../02_ADR/network-improvement.md)

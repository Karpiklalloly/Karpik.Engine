# Улучшение сетевой подсистемы

> 📅 Дата: 2026-03-28 | Автор: Atlas | Статус: Планирование

## 📋 Текущее состояние

| Компонент | Реализация |
|-----------|------------|
| Snapshot | Полный, каждый тик, Unreliable |
| Интерполяция | ❌ Нет |
| Предикция | ❌ Нет |
| Reconciliation | ❌ Нет |
| Deduplication | ❌ Нет |
| Peer iteration | ❌ Только FirstPeer |

### Архитектура

```
Server                                          Client
───────                                        ──────
NetworkSystem                                    MyGameClientInstaller
    │                                                 │
    ├── SendSnapshotToAll() ◄────────────────────────┼── ApplySnapshot()
    │       │                                             │
    │       └── WriteSnapshot()                         └── InterpolationSystem (TODO)
    │               │                                           │
    │               └── [Full snapshot every tick]            └── [Lerp between states]
    │
    └── Receive Command
            │
            └── CommandDispatcher
```

---

## 🎯 Фазы реализации

### Фаза 1: Интерполяция ⭐

> **Цель**: Плавное движение чужих игроков

#### 1.1 State Buffer

```
Goal: Хранить последние N состояний для интерполяции
```

- [ ] Компонент `NetworkStateBuffer` — FixedQueue<NetworkState>
- [ ] NetworkState: timestamp, position, rotation, velocity
- [ ] Сохранять позицию при получении снапшота с timestamp

#### 1.2 Interpolation System

```
Goal: Плавное движение чужих игроков
```

- [ ] `InterpolationSystem` — применяет интерполяцию между состояниями
- [ ] `InterpolationDelay` — задержка (100-150ms) для сглаживания
- [ ] Lerp для position, rotation
- [ ] Snap на first connect

#### 1.3 Тестирование

- [ ] Запуск 2+ клиентов
- [ ] Проверка плавности при 5%, 10% packet loss

**Сложность**: ★☆☆ | **Время**: 1-2 недели | **Риск**: Низкий

---

### Фаза 2: Предикция своего персонажа

> **Цель**: Мгновенная реакция на ввод

#### 2.1 Client-Side Prediction

```
Goal: Мгновенная реакция на ввод
```

- [ ] `InputHistory` — буфер отправленных команд (sequence number)
- [ ] `PredictedPosition` — компонент для предикции
- [ ] Применять ввод локально ДО отправки на сервер
- [ ] Сохранять копию состояния перед предикцией

#### 2.2 Server Reconciliation

```
Goal: Коррекция расхождений с сервером
```

- [ ] При получении снапшота — сравнить с предсказанным
- [ ] Если delta < threshold — smooth correction
- [ ] Если delta > threshold — rollback + replay input history

#### 2.3 Тестирование

- [ ] High latency simulation (200ms)
- [ ] Проверка rollback при телепортации

**Сложность**: ★★☆ | **Время**: 2-3 недели | **Риск**: Средний

---

### Фаза 3: Оптимизация

> **Цель**: Уменьшить трафик

#### 3.1 Delta Compression

```
Goal: Уменьшить трафик
```

- [ ] Отправлять только changed components
- [ ] Dirty flags для компонентов

#### 3.2 Incremental Snapshots

```
Goal: Не отправлять всё каждый тик
```

- [ ] Full snapshot раз в N тиков (每秒 10)
- [ ] Incremental — только diff между full snapshots

**Сложность**: ★★☆ | **Время**: 2 недели | **Риск**: Средний

---

### Фаза 4: Расширенные фичи (опционально)

> **Цель**: Продвинутые сетевые фичи

#### 4.1 Client Authority

- [ ] authority transfer для статических объектов
- [ ] собственные предметы игроков

#### 4.2 Lag Compensation

- [ ] Реконструкция состояния сервера для hit detection
- [ ] Raycast с поправкой на latency

**Сложность**: ★★★ | **Время**: 2+ недели | **Риск**: Высокий

---

## 📁 Файлы для изменения

| Файл | Изменение |
|------|-----------|
| `NetworkManager.g.cs` | Добавить timestamp к snapshot |
| `ApplySnapshot` → новый | Интерполяция между снапшотами |
| Новая система | InterpolationSystem |
| Новая система | PredictionSystem |

---

## 🔗 Зависимости между фазами

```
Фаза 1 ─────────────► Фаза 2 ───────────► Фаза 3
    │                    │                   │
    │                    │                   │
    └─ State Buffer      └─ Input History    └─ Delta Comp
    └─ Interpolation     └─ Reconciliation   └─ Incremental
```

---

## 📊 Оценка времени

| Фаза | Сложность | Время | Риск |
|------|-----------|-------|------|
| 1. Интерполяция | ★☆☆ | 1-2 нед | Низкий |
| 2. Предикция | ★★☆ | 2-3 нед | Средний |
| 3. Оптимизация | ★★☆ | 2 нед | Средний |
| 4. Authority | ★★★ | 2+ нед | Высокий |

---

## 🎯 Приоритеты

**Рекомендуемый порядок:**

1. **Фаза 1** — интерполяция — самый большой визуальный эффект
2. **Фаза 2** — предикция — если лаги при высоком пинге критичны

---

## Дополнительные ресурсы

- [Network.Shared модуль](modules/shared/network-shared.md)
- [Network.Server модуль](modules/server/network-server.md)
- [Network.Client модуль](modules/client/network-client.md)
- [ECS документация](modules/shared/ecs.md)

---
title: "Lua code revisions поверх постоянного ECS-хранилища"
date: "2026-11-14"
status: "accepted"
tags:
  - adr
  - architecture
  - hot-reload
---

# Lua code revisions поверх постоянного ECS-хранилища

## Context

Simulation scope находится под неизменным ModSet. Его пересоздание уничтожает мир и services; переинициализация активного EcsUpdateScheduler освобождает старое execution state ещё до проверки нового графа. ComponentQueryPreparer прежде владел одновременно dynamic storage, queries и command buffers, поэтому disposal привязок уничтожал gameplay state. Клиент одновременно использует simulation worker и main-thread rendering; пауза одного потока недостаточна.

## Decision

Сохранить цепочку Engine -> ModSet -> Simulation. ModSet хранит фиксированные definitions/schemas. Lua executable code может иметь последовательно активируемые revision внутри Simulation при прежнем наборе модов/схем. Это уточняет исторический общий modding design, где все prepared programs относились только к ModSet; полный ModSet reload/migration остаётся отдельным решением.

Persistent world/native/dynamic pools и pointer policy принадлежат Simulation. Factory-owned Lua revision aggregate владеет VM, системами, capabilities/callback scope, query listeners и bounded command scratch. Root preparer остаётся factory persistent storage после retirement первой revision; его final Dispose инвалидирует все live revisions и освобождает storage. Дочерняя revision не владеет данными мира.

Готовить отдельный scheduler-кандидат с теми же экземплярами CLR-систем и новыми Lua-adapters. В явной maintenance-паузе дождаться завершения текущего frame/jobs, удерживать client worker reservation и исключить render/frame callbacks. Main-thread control queue продолжает работать без кадров; snapshot/state requests отклоняются. Никакие candidate systems не запускаются до проверки общего графа, on_reload и Commit.

Commit публикует runtime revision и scheduler при закрытом barrier, без Lua/IO. После него отозвать старые capabilities и освободить старый scheduler/VM/listeners. Ошибка подготовки сохраняет старую версию; ошибка Lua cleanup-hook логируется, ошибка host cleanup/Commit останавливает Simulation. Rollback после Commit запрещён. Нет повторного on_start/initial spawn, legacy и declared callbacks читают одну revision.

## Alternatives

- Пересоздавать Simulation и восстанавливать snapshot: проще использовать существующий worker restart, но нарушает цель сохранения той же Simulation/ECS и не выбрано.
- Менять ModSet parent существующей Simulation: несовместимо с текущим DI lifetime tree без пересоздания ребёнка.
- Создавать DI child scope для каждой Lua revision: расширяет Static/factory/lifetime поверхность; explicit aggregate с constructor-injected factory достаточен для динамических runtime данных.
- Общий executor с заменяемым prepared state: экономит peak workers/buffers, но требует более глубокого scheduler refactor. Для первого среза выбран отдельный проверяемый кандидат; цена измеряется.
- Фоновая подготовка при продолжающихся тиках: снижает паузу, требует дополнительной thread/data isolation и отложена.

## Consequences

Обычный common tick остаётся без новых allocations и locks; gate использует предсказуемые атомарные счётчики. Maintenance может выделять память и временно держать два набора scheduler workers/буферов. Пауза и peak resources измеряются отдельно; SLA для Lua не обещается. Свежие snapshots/logs/скрипты не означают state migration: VM locals теряются, сохраняются ECS компоненты.

Local host явно включает режим до Setup. Односторонний reload не меняет metadata, набор, схемы или assets и недоступен в сетевом составе. Cancellation cooperative; зависшую VM нельзя безопасно прервать этим API.

Согласованный переход расширяет этот ADR без замены DI-дерева: ModSet держит стартовый anchor, Simulation — принятый набор и текущий каталог. Совместимые плотные пулы и нетронутые VM сохраняются; `inc`/`opt`/`exc` и wire/host-домены регистрируют заимствования. Снятие пула с живыми строками или оставшимися привязками запрещено. Хост с live-переходом создаёт доступные новым Lua queries нативные CLR-пулы при старте Simulation: одна регистрация типа не создаёт пул, а staged-кандидат не публикует нативные пулы до Commit. Декларативная Debug-миграция готовит только затронутые пулы до Commit; Release Apply не меняет схему остающегося мода. Последовательные Lua callbacks разделяют один командный буфер с проверкой домена, системы и вызова. См. [ExecPlan смены набора](../../plans/modding-live-set-execplan.md) и [сетевой ADR](mod-live-revision-protocol.md).

## Verification

[ExecPlan](../../plans/lua-hot-reload-execplan.md) содержит актуальный acceptance/review status; [evidence](../../plans/lua-hot-reload-evidence.md) — команды/результаты. Проверяются Static/Dynamic, Deterministic/Parallel, SingleThread smoke, реальные файлы, same entity generations, failure/cancellation/retirement, worker barrier, fixed time remainder, повторные reload и освобождение старых VM. Решение архитектуры принято пользователем; завершённость реализации определяется проверками, а не этим ADR.

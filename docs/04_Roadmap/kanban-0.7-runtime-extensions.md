---

kanban-plugin: board

---

## KarpikEngine 0.7 — Runtime Extensions

Перенесено из 0.6 по решению разработчика 2026-10-01. Существующие renderer, Camera2D и keyboard/mouse snapshot API остаются доступными в 0.6. Координаты, content/editor и приёмка текущей версии остаются в [доске 0.6](kanban-0.6-2d-runtime.md).

Исходный объём 0.7 (scenes, prefabs, tilemaps, audio) сохраняется в [общей roadmap](karpikengine-1.0-roadmap.md#07-authoring-content).

## Архитектурные решения

- [ ] **Определить публичный `IRenderer`**
	  - Что сделать: спроектировать небольшой façade над существующими command buffers; оставить низкоуровневый API доступным для особых случаев.
	  - Зачем: обычный игровой код должен рисовать без знания устройства буферов, потоков merge и backend деталей.
	  - Результат: стабильный client-only контракт, не создающий объектов на кадр и не протекающий типами backend-а в Shared.

## Renderer

- [ ] **Расширить базовый 2D renderer**
	  - Что сделать: реализовать `DrawTexture`, `DrawSprite`, `DrawRect`, `DrawText`, `DrawLine`; добавить source rect/UV, origin, rotation, scale, flip, tint, screen/world space и layer depth.
	  - Зачем: это минимальный выразительный набор для игрового мира, HUD и отладки.
	  - Результат: все операции проходят через batching/sorting без выделений в render prepare; есть пример сцены, использующий каждую операцию.
- [ ] **Добавить render states и цели рендеринга**
	  - Что сделать: поддержать render targets, blend/sampler modes, viewport и scissor; определить безопасные значения по умолчанию.
	  - Зачем: без этого невозможны нормальные эффекты, отсечения, отдельные UI/scene passes и предсказуемый backend state.
	  - Результат: API не требует прямого доступа к Graphics backend, а смены state видны в diagnostics.
- [ ] **Сделать диагностику рендера**
	  - Что сделать: добавить счётчики draw calls, batches, texture/pipeline/render-target switches, submitted quads и text glyphs; показать их в ImGui overlay.
	  - Зачем: оптимизация 2D renderer должна опираться на измерения, а не на предположения.
	  - Результат: разработчик видит стоимость кадра и может подтвердить, что batching действительно работает.
- [ ] **Сделать рендеринг текста пригодным для игры**
	  - Что сделать: завершить atlas font path, выбрать SDF/MSDF объём для 1.0, добавить layout, glyph caching и fallback diagnostics.
	  - Зачем: UI, HUD и игровые подписи не должны строить строки/глифы с выделениями на каждом кадре или молча терять символы.
	  - Результат: текст рендерится без выделений в steady state; отсутствующий символ выдаёт понятную диагностику.

## Input

- [ ] **Довести Input до игрового набора устройств**
	  - Что сделать: добавить gamepad buttons/sticks/triggers и connection lifecycle; решить, нужен ли touch abstraction для 1.0.
	  - Зачем: текущая клавиатура/мышь покрывает desktop sample, но не типичную 2D игру с геймпадом.
	  - Результат: snapshot API остаётся без выделений; отключение/подключение устройства не оставляет залипших состояний.
- [ ] **Добавить actions и переназначение ввода**
	  - Что сделать: actions, axes, bindings, profiles, runtime rebinding и хранение профиля через config/assets.
	  - Зачем: gameplay не должен зависеть от конкретных клавиш, а игрок должен менять управление без отдельного редактора.
	  - Результат: один action mapping работает для клавиатуры и gamepad; профиль переживает перезапуск.

## Базовые утилиты

- [ ] **Добавить VFS, save/config и debug draw**
	  - Что сделать: смонтированные asset folders, user-data path и нормализацию путей; versioned save/config; линии, фигуры, collider/tile-grid debug draw.
	  - Зачем: игре нужны безопасные пользовательские данные, настройки и быстрая визуальная диагностика без зависимости от конкретной платформы.
	  - Результат: save/config roundtrip покрыт тестом, а debug draw интегрирован с камерой и overlay.

## Камеры

- [ ] **Завершить Camera2D**
	  - Что сделать: добавить camera-relative rendering и, если API остаётся простым, несколько камер.
	  - Зачем: камера должна работать в больших мирах и позволять split view/minimap без дублирования renderer-а.
	  - Результат: world-to-screen и screen-to-world остаются согласованными, а camera-relative boundary следует принятому правилу точности.

## После измерений / при необходимости

- [ ] **Build-time sprite atlases**
	  - Что сделать: manifest, детерминированная упаковка, padding/bleeding protection, несколько страниц и generated UV metadata.
	  - Зачем: атласы уменьшают texture switches, но не должны ломать строгий painter order прозрачных команд.
	  - Начинать только когда diagnostics покажут реальный multi-texture bottleneck; текущий texture-thrash тест намеренно синтетический.
	  - Результат: sample доказывает строгий порядок рендеринга нескольких спрайтов из одного atlas и один resource set.
- [ ] **Несколько Camera2D**
	  - Что сделать: добавить только после того, как базовый API будет использоваться реальным HUD/minimap/split-screen сценарием.
	  - Зачем: не усложнять lifecycle и render target ownership без потребителя.
	  - Результат: каждая камера имеет независимый viewport и не смешивает command ordering другой камеры.

%% kanban:settings
```
{"kanban-plugin":"board","list-collapse":[null]}
```
%%

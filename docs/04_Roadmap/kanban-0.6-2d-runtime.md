---

kanban-plugin: board

---

## KarpikEngine 0.6 — 2D Runtime Core



## Сначала: архитектурные решения

- [ ] **Зафиксировать систему координат и границу точности**
	  - Что сделать: исследовать текущий Physics2D backend; выбрать, где runtime хранит `double`, а где остаётся `float`; описать правило преобразования координат камеры для рендера.
	  - Зачем: большие миры не должны терять точность, а рендер и физика должны иметь явную, а не случайную границу типов.
	  - Результат: ADR/документ с правилами для transform, camera, physics, tilemap, scenes и networking; решение по full-double physics либо перенос в post-1.0.
- [ ] **Определить публичный `IRenderer`**
	  - Что сделать: спроектировать небольшой façade над существующими command buffers; оставить низкоуровневый API доступным для особых случаев.
	  - Зачем: обычный игровой код должен рисовать без знания устройства буферов, потоков merge и backend деталей.
	  - Результат: стабильный client-only контракт, не создающий объектов на кадр и не протекающий типами backend-а в Shared.
- [ ] **Определить контракт ресурсов и идентификаторов ассетов**
	  - Что сделать: выбрать typed handles, стабильные ID, manifest и модель dependency invalidation.
	  - Зачем: сцены, prefabs и tilemaps поздних релизов должны ссылаться на контент без хрупких путей и неявного времени жизни.
	  - Результат: спецификация формата manifest и правил загрузки, освобождения, hot reload и ошибок отсутствующего ресурса.
	  - План реализации: [kanban-content-pipeline-approach-2.md](kanban-content-pipeline-approach-2.md).


## Готово к реализации

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
- [ ] **Завершить Camera2D**
	  - Что сделать: добавить camera-relative rendering и, если API остаётся простым, несколько камер.
	  - Зачем: камера должна работать в больших мирах и позволять split view/minimap без дублирования renderer-а.
	  - Результат: world-to-screen и screen-to-world остаются согласованными, а camera-relative boundary следует принятому правилу точности.
- [ ] **Сделать рендеринг текста пригодным для игры**
	  - Что сделать: завершить atlas font path, выбрать SDF/MSDF объём для 1.0, добавить layout, glyph caching и fallback diagnostics.
	  - Зачем: UI, HUD и игровые подписи не должны строить строки/глифы с выделениями на каждом кадре или молча терять символы.
	  - Результат: текст рендерится без выделений в steady state; отсутствующий символ выдаёт понятную диагностику.
- [ ] **Построить минимальный content pipeline**
	  - Что сделать: manifest, typed handles, stable IDs, dependency graph, processors для textures/fonts/shaders/data/tilemaps, CLI asset build и validation.
	  - Зачем: контент должен собираться воспроизводимо до запуска, а изменения должны адресно обновлять зависимые ресурсы.
	  - Результат: Debug и Release используют одинаковый результат сборки; клиент получает hot reload notification без полного перезапуска.
- [ ] **Довести Input до игрового набора устройств**
	  - Что сделать: добавить gamepad buttons/sticks/triggers и connection lifecycle; решить, нужен ли touch abstraction для 1.0.
	  - Зачем: текущая клавиатура/мышь покрывает desktop sample, но не типичную 2D игру с геймпадом.
	  - Результат: snapshot API остаётся без выделений; отключение/подключение устройства не оставляет залипших состояний.
- [ ] **Добавить actions и переназначение ввода**
	  - Что сделать: actions, axes, bindings, profiles, runtime rebinding и хранение профиля через config/assets.
	  - Зачем: gameplay не должен зависеть от конкретных клавиш, а игрок должен менять управление без отдельного редактора.
	  - Результат: один action mapping работает для клавиатуры и gamepad; профиль переживает перезапуск.
- [ ] **Добавить VFS, save/config и debug draw**
	  - Что сделать: смонтированные asset folders, user-data path и нормализацию путей; versioned save/config; линии, фигуры, collider/tile-grid debug draw.
	  - Зачем: игре нужны безопасные пользовательские данные, настройки и быстрая визуальная диагностика без зависимости от конкретной платформы.
	  - Результат: save/config roundtrip покрыт тестом, а debug draw интегрирован с камерой и overlay.
- [ ] **Сделать базовый редактор**
	  - Стек: Avalonia 12 + Dock 12 + ReactiveUI; ImGui остаётся только runtime/debug overlay. См. [ADR](../02_ADR/editor-desktop-stack.md).
	  - Доска выполнения: [KarpikEngine 0.6 — Базовый редактор](kanban-0.6-editor.md).
	  - Первый срез до content pipeline: открыть существующий проект, запустить/остановить preview, просмотреть логи, иерархию сущностей и ECS-компоненты только для чтения.
	  - Что сделать: отдельное editor-приложение с project/asset browser, dockable Scene/Game/Console/Hierarchy/Inspector панелями, просмотром ECS state и разделением edit/play mode. Veldrid/SDL2 preview в 0.6 работает отдельно от Avalonia visual tree.
	  - Зачем: к завершению 0.6 разработчик должен проверять сцену, ассеты и состояние игры без ручной отладки и без влияния tooling на runtime hot paths.
	  - Результат: editor открывает проект, показывает manifest-ассеты, сцену и ECS-компоненты в preview; редактирование компонентов, scene authoring и gizmos остаются для 0.7/1.1.


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


## Не входит в 1.0

- [ ] **Визуальный редактор переназначения ввода и абстракция уровня Steam Input**
	  - Причина: базовые actions/profiles закрывают игровой runtime; визуальный редактор и широкая платформенная абстракция требуют отдельного инструментария.
- [ ] **Полная double-precision физика, если текущий backend требует непропорционального переписывания**
	  - Причина: граница координат должна быть задокументирована, но переписывание проверенного 2D backend-а не оправдано без требований игры.




%% kanban:settings
```
{"kanban-plugin":"board","list-collapse":[null]}
```
%%

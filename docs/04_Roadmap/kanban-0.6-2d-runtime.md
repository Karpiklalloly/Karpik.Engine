---

kanban-plugin: board

---

## KarpikEngine 0.6 — 2D Runtime Core

Перенос согласован 2026-10-01: renderer, input, базовые утилиты, атласы и дальнейшее развитие камер — в [0.7](kanban-0.7-runtime-extensions.md). Координаты, content/editor и приёмка остаются в 0.6. Правило camera-relative границы точности описывается в 0.6; реализация расширений камеры — в 0.7.



## Сначала: архитектурные решения

- [ ] **Зафиксировать систему координат и границу точности**
	  - Что сделать: исследовать текущий Physics2D backend; выбрать, где runtime хранит `double`, а где остаётся `float`; описать правило преобразования координат камеры для рендера.
	  - Зачем: большие миры не должны терять точность, а рендер и физика должны иметь явную, а не случайную границу типов.
	  - Результат: ADR/документ с правилами для transform, camera, physics, tilemap, scenes и networking; решение по full-double physics либо перенос в post-1.0.
- [ ] **Определить контракт ресурсов и идентификаторов ассетов**
	  - Что сделать: выбрать typed handles, стабильные ID, manifest и модель dependency invalidation.
	  - Зачем: сцены, prefabs и tilemaps поздних релизов должны ссылаться на контент без хрупких путей и неявного времени жизни.
	  - Результат: спецификация формата manifest и правил загрузки, освобождения, hot reload и ошибок отсутствующего ресурса.
	  - План реализации: [kanban-content-pipeline-approach-2.md](kanban-content-pipeline-approach-2.md).


## Готово к реализации

- [ ] **Построить минимальный content pipeline**
	  - Что сделать: manifest, typed handles, stable IDs, dependency graph, processors для textures/fonts/shaders/data/tilemaps, CLI asset build и validation.
	  - Зачем: контент должен собираться воспроизводимо до запуска, а изменения должны адресно обновлять зависимые ресурсы.
	  - Результат: Debug и Release используют одинаковый результат сборки; клиент получает hot reload notification без полного перезапуска.
- [ ] **Сделать базовый редактор**
	  - Стек: Avalonia 12 + Dock 12 + ReactiveUI; ImGui остаётся только runtime/debug overlay. См. [ADR](../02_ADR/editor-desktop-stack.md).
	  - Доска выполнения: [KarpikEngine 0.6 — Базовый редактор](kanban-0.6-editor.md).
	  - Первый срез до content pipeline: открыть существующий проект, запустить/остановить preview, просмотреть логи, иерархию сущностей и ECS-компоненты только для чтения.
	  - Что сделать: отдельное editor-приложение с project/asset browser, dockable Scene/Game/Console/Hierarchy/Inspector панелями, просмотром ECS state и разделением edit/play mode. Veldrid/SDL2 preview в 0.6 работает отдельно от Avalonia visual tree.
	  - Зачем: к завершению 0.6 разработчик должен проверять сцену, ассеты и состояние игры без ручной отладки и без влияния tooling на runtime hot paths.
	  - Результат: editor открывает проект, показывает manifest-ассеты, сцену и ECS-компоненты в preview; редактирование компонентов, scene authoring и gizmos остаются для 0.7/1.1.


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

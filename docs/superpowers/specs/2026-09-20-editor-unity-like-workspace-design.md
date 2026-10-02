# Unity-подобная рабочая поверхность Karpik Editor

## Цель

Сделать `Karpik.Editor` визуально и поведенчески ближе к Unity Editor: компактный тёмный интерфейс, привычное расположение панелей, перетаскиваемые вкладки, верхнее меню с быстрыми командами и хоткеями.

Первый срез меняет рабочую оболочку редактора. Он не добавляет авторинг сцены, редактирование ECS-компонентов, gizmo-инструменты или настоящий undo/redo для игровых данных.

## Согласованный UX

### Раскладка

- Слева — `Hierarchy` и `Sessions` как вкладки одной dock-зоны.
- В центре — `Scene`, `Game` и `Preview` как документы одного `DocumentDock`.
- Справа — `Inspector`.
- Снизу — `Project` и `Console` как вкладки нижней dock-зоны.
- Все вкладки можно reorder-перетаскиванием; поддерживаются dock в другую зону и floating, если это разрешено для конкретной панели.
- Раскладка сохраняется существующим `DockLayoutStore`.

В `Editor Settings` пользователь выбирает раскладку окон:

- `Unity` — согласованная основная схема с Hierarchy слева, Scene/Game/Preview в центре, Inspector справа и Project/Console снизу;
- `Debug` — та же оболочка, но с увеличенной нижней зоной для Console и видимой группой Sessions;
- `Custom` — текущая пользовательская раскладка со всеми перетаскиваниями, размерами и порядком вкладок.

`Custom` не сбрасывается при выборе готовой схемы. Перед переключением на `Unity` или `Debug` текущий dock tree сохраняется как пользовательский; возврат в `Custom` восстанавливает его. Готовые схемы генерируются из кода и не перезаписывают сохранённую пользовательскую раскладку. Применение готовой схемы выполняется явно кнопкой `Apply`.

Центральные документы используют единый набор вкладок. Активный документ меняет только центральное содержимое и не создаёт отдельный runtime или ECS-мир.

### Плотность

По умолчанию используется `Compact`:

- меню: около 16–18 px;
- toolbar: около 20 px;
- вкладки: около 18–20 px;
- списки: около 15–17 px на строку;
- минимальные отступы и короткие подписи кнопок.

Пользователь может выбрать `Compact`, `UltraCompact` или `Large` в отдельном окне `Window → Editor Settings…`. Режим хранится в пользовательском workspace, а не в игровом проекте и не в ассетах. Выбор плотности и выбор раскладки находятся в одном окне настроек.

### Цветовая тема

Используется тёмная графитовая палитра в Unity-подобном стиле:

- окно: `#1E1E1E`;
- панели: `#252526`;
- toolbar и заголовки: `#2D2D30`;
- выделение: `#3E6EAA`;
- стандартный текст: светло-серый;
- `▶S` и `▶C` имеют разные, но приглушённые акцентные цвета.

Цвета должны быть Avalonia resources/styles, а не повторяться в каждом контроле.

## Верхнее меню и команды

Главное меню содержит:

- `File`: Open Project, Save Workspace, Save Workspace As, Close Project, Exit;
- `Edit`: Undo, Redo, Frame Selected, Delete;
- `Assets`: обновление/операции с ассетами, доступные текущему проекту;
- `GameObject`: будущие операции авторинга сцены;
- `Window`: панели и `Editor Settings…`;
- `Help`.

`Undo` и `Redo` находятся только в `Edit`, не в toolbar. До появления undoable-операций авторинга сцены эти пункты могут быть disabled; нельзя имитировать историю действий пустыми командами.

Toolbar содержит только часто используемые существующие операции:

- `▶S` — `StartServerCommand`;
- `▶C` — `AddClientCommand`;
- остановка всех сессий — `StopAllCommand`;
- сборка и публикация проекта;
- проверка runtime, если действие помещается в доступную ширину.

Кнопки меню и toolbar должны ссылаться на одни и те же существующие `ReactiveCommand` в `EditorShellViewModel`. Новая глобальная command bus, фабрика команд или слой-обёртка не создаётся.

Минимальные хоткеи:

- `Ctrl+O` — открыть проект;
- `Ctrl+S` — сохранить workspace;
- `Ctrl+Shift+S` — сохранить workspace как;
- `Ctrl+Z` / `Ctrl+Shift+Z` — Undo / Redo;
- `F` — сфокусироваться на выбранном объекте, когда появится Scene View;
- `Delete` — удалить выбранный объект, когда появится авторинг сцены;
- `Alt+F4` — закрыть редактор.

Хоткеи задаются нативными Avalonia `KeyBinding`/`HotKey`, а не ручным глобальным обработчиком клавиатуры.

## Изменения в коде

Основные точки изменения:

- `Karpik.Editor/MainWindow.axaml` — компактные styles, меню, toolbar, key bindings и шаблоны вкладок;
- `Karpik.Editor/MainWindow.axaml.cs` — открытие/закрытие окна `Editor Settings` и существующие file-dialog handlers;
- `Karpik.Editor/Docking/EditorDockFactory.cs` — группы dockable-панелей, три центральных `Document` и генерация preset layouts;
- `Karpik.Editor/Docking/DockLayoutStore.cs` — сохранение текущего и пользовательского dock tree;
- `Karpik.Editor/Models/EditorWorkspace.cs` — enum плотности, enum раскладки и их JSON persistence;
- `Karpik.Editor` settings view/window — выбор плотности и раскладки с явным Apply;
- `Karpik.Editor/ViewModels/EditorShellViewModel.cs` — только команды/состояния, которых действительно не хватает для меню;
- `Karpik.Editor/App.axaml` или отдельный editor style resource — графитовые цвета и размеры контролов;
- `Karpik.Editor.Tests/EditorDockFactoryTests.cs` — структура dock tree, вкладки и ограничения float/close;
- `Karpik.Editor.Tests/EditorWorkspaceTests.cs` — round-trip выбранной плотности;
- новые тесты settings view-model — только если настройки не удастся покрыть существующим workspace тестом.

Не меняются `Client`, `Server`, `Shared`, ECS snapshot contracts и runtime hot paths.

## Жизненный цикл и границы

Editor остаётся отдельным Avalonia composition root. Панели получают существующие view-model contexts через `EditorDockFactory`. Перетаскивание и сохранение вкладок меняют только UI layout; они не создают ECS entities, runtime sessions или сетевые соединения.

`Preview` остаётся отделённым от Avalonia rendering lifecycle согласно ADR о desktop stack. Встраивание Veldrid/SDL2 в visual tree не входит в этот срез.

## Проверка

1. Собрать минимальный проект редактора с `dotnet build Karpik.Editor/Karpik.Editor.csproj -m:1 -nr:false`.
2. Запустить targeted tests проекта `Karpik.Editor.Tests` с теми же ограничениями MSBuild.
3. Проверить вручную:
   - стартовое окно использует Compact и графитовую тему;
   - `Scene`, `Game`, `Preview` находятся в одной центральной группе;
   - вкладки можно reorder/dock, а раскладка восстанавливается после перезапуска;
   - `Unity`, `Debug` и `Custom` выбираются в `Editor Settings`;
   - переход `Custom → Unity/Debug → Custom` возвращает пользовательские размеры, порядок и dock-зоны;
   - `Project` и `Console` находятся в нижней группе;
   - `▶S` запускает сервер, `▶C` добавляет клиента, Stop/Build/Publish сохраняют текущее поведение;
   - Undo/Redo показываются в `Edit`, но не заявляют несуществующую историю;
   - `Editor Settings` меняет плотность без изменения игрового проекта или ассетов;
   - открытие/остановка preview и выбор snapshot source продолжают работать.

## Не входит в этот срез

- полноценный Scene View;
- редактирование ECS-компонентов;
- transform gizmos;
- prefab и tilemap authoring;
- undo/redo stack для игровых данных;
- command palette;
- произвольное создание и именование дополнительных layout presets;
- изменение runtime/client/server контрактов;
- embedded Veldrid viewport.

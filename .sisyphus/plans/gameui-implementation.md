# GameUI Implementation Plan

> Created: 2026-03-25
> Status: In Progress

## Overview

Реализация модуля GameUI для KarpikEngine согласно архитектуре из `docs/modules/client/ui.md`.

## Pre-requisites

- [ ] **P0** Удалить старый UI модуль (`Modules/Client/UI/`)
- [ ] **P0** Удалить ссылки на старый UI из других проектов

## TODOs

- [ ] Phase 1: Core — базовые типы, IRenderer, Widgets структуры
- [ ] Phase 2: Layout — flexbox система
- [ ] Phase 3: Events — callbacks, events, bubbling
- [ ] Phase 4: Styles — CSS-like стили, селекторы
- [ ] Phase 5: Input — hit testing, interaction states
- [ ] Phase 6: MVVM — IViewModel, binding
- [ ] Phase 7: Tables — виртуализация

---

## Phase 1: Core (MVP)

### Задачи

- [ ] **1.1** Создать структуру проектов GameUI.Core и GameUI.EngineNative
- [ ] **1.2** Rectangle, Color, Vector2, Size типы
- [ ] **1.3** UiTypeId enum
- [ ] **1.4** IRenderer интерфейс
- [ ] **1.5** UIWidget структура с иерархией (Parent/Child/Sibling индексы)
- [ ] **1.6** WidgetData структуры (ButtonData, LabelData, ImageData, etc.)
- [ ] **1.7** WidgetStorage — хранение виджетов в массивах
- [ ] **1.8** WidgetTree — обход дерева

### Definition of Done

- [ ] Проекты созданы и компилируются
- [ ] Базовые типы работают
- [ ] Иерархия виджетов создаётся и обходится
- [ ] IRenderer имеет базовую реализацию (через Graphics модуль)

### Acceptance Criteria

```
[ ] Rectangle, Color, Vector2 — работают
[ ] UIWidget с индексами Parent/Child/Sibling — создаётся
[ ] WidgetTree.Traverse() — обходит дерево
[ ] IRenderer — базовые методы (DrawRect, DrawText)
```

---

## Phase 2: Layout

### Задачи

- [ ] **2.1** FlexContainerStyle структура (Direction, Justify, Align, Gap, Wrap)
- [ ] **2.2** LayoutEngine — вычисление preferred size
- [ ] **2.3** LayoutEngine — flexbox алгоритм
- [ ] **2.4** WidgetStorage — метод Add с layout параметрами
- [ ] **2.5** Layout dirty flags — пересчёт только при изменении

### Definition of Done

- [ ] Flexbox layout работает для Row/Column
- [ ] Justify (Start/Center/End/SpaceBetween/SpaceAround) работает
- [ ] Align (Start/Center/End/Stretch) работает
- [ ] Gap работает

### Acceptance Criteria

```
[ ] Container с Direction=Row — элементы в ряд
[ ] Container с Direction=Column — элементы в колонку
[ ] Justify=Center — по центру
[ ] Gap=8 — отступ 8px между элементами
[ ] Bounds вычисляются правильно
```

---

## Phase 3: Events

### Задачи

- [ ] **3.1** Callback delegates (OnClick, OnHover, OnPress)
- [ ] **3.2** Events (для нескольких подписчиков)
- [ ] **3.3** WidgetStorage — методы для подписки
- [ ] **3.4** Event firing — вызов при взаимодействии
- [ ] **3.5** Bubbling — подъём событий к родителю

### Definition of Done

- [ ] Callbacks работают
- [ ] Events работают (несколько подписчиков)
- [ ] Bubbling работает

### Acceptance Criteria

```
[ ] button.OnClick += handler — вызывается при клике
[ ] button.OnClick -= handler — отписка работает
[ ] bubble events — событие доходит до root
```

---

## Phase 4: Styles

### Задачи

- [ ] **4.1** UIStyle класс с пулом
- [ ] **4.2** Свойства стиля (Background, TextColor, Padding, Margin, Border, CornerRadius, FontSize)
- [ ] **4.3** ResourceDictionary — хранение глобальных ресурсов
- [ ] **4.4** Style селекторы (Type, Class, ID)
- [ ] **4.5** Pseudo-states (:hover, :active)
- [ ] **4.6** Каскад — применение стилей по приоритету
- [ ] **4.7** Theme загрузка из .uitheme файлов

### Definition of Done

- [ ] Стили применяются к виджетам
- [ ] CSS-like селекторы работают
- [ ] Каскад работает (ID > Class > Type)
- [ ] Themes загружаются

### Acceptance Criteria

```
[ ] button.Style = ButtonStyle — применяется
[ ] button.AddClass("primary") — применяется
[ ] button.Id = "ok" — применяется
[ ] :hover — меняется стиль при наведении
[ ] $resource — подставляется значение из Resources
```

---

## Phase 5: Input

### Задачи

- [ ] **5.1** HitTest — простой поиск элемента под курсором
- [ ] **5.2** Z-index сортировка
- [ ] **5.3** InteractionState (Normal, Hovered, Pressed, Focused, Disabled)
- [ ] **5.4** Keyboard focus handling
- [ ] **5.5** Quadtree (опционально, если понадобится)

### Definition of Done

- [ ] HitTest находит элемент под курсором
- [ ] Hover state меняется
- [ ] Press state меняется

### Acceptance Criteria

```
[ ] HitTest(mousePos) — возвращает виджет под курсором
[ ] При наведении — Hovered state
[ ] При нажатии — Pressed state
[ ] При клике — Click event
```

---

## Phase 6: MVVM

### Задачи

- [ ] **6.1** IViewModel интерфейс
- [ ] **6.2** Bindable<T> — для двустороннего binding
- [ ] **6.3** ICommand интерфейс
- [ ] **6.4** Command базовая реализация
- [ ] **6.5** UIBuilder с binding методами
- [ ] **6.6** Пример меню с MVVM

### Definition of Done

- [ ] Bindable работает
- [ ] Binding обновляет UI при изменении данных
- [ ] Commands работают

### Acceptance Criteria

```
[ ] slider.BindValue(vm.Volume) — изменение value обновляет Volume
[ ] button.BindCommand(vm.Save) — клик вызывает Save
[ ] vm.Volume = 50 — UI обновляется
```

---

## Phase 7: Tables (VirtualGrid)

### Задачи

- [ ] **7.1** VirtualGrid компонент
- [ ] **7.2** Рендер только видимых ячеек
- [ ] **7.3** CellData массив (без ECS)
- [ ] **7.4** Scroll handling
- [ ] **7.5** Inline editing ячеек

### Definition of Done

- [ ] 10,000+ ячеек рендерятся без лагов
- [ ] Scroll работает плавно
- [ ] Клик на ячейку — редактирование

### Acceptance Criteria

```
[ ] Grid 100x100 — рендерит только видимые (20x10)
[ ] Scroll — плавный
[ ] Клик на ячейку — активирует редактирование
```

---

## Final Verification

### F1: Компиляция

```
[ ] dotnet build — без ошибок
[ ] Все проекты ссылаются правильно
```

### F2: Тесты

```
[ ] Unit тесты для LayoutEngine
[ ] Unit тесты для стилей
[ ] Тесты для hit testing
```

### F3: Интеграция

```
[ ] GameUI работает с Graphics модулем
[ ] Виджеты рендерятся
[ ] Events срабатывают
```

### F4: Документация

```
[ ] Код документирован (XML comments)
[ ] Примеры работают
```

---

## Notes

- Markup (UI файлы) — пока отложено, в будущих фазах
- Quadtree — только если понадобится для производительности
- Тесты писать параллельно с кодом

# KarpikEngine UI System

Полнофункциональная система пользовательского интерфейса для игрового движка KarpikEngine, построенная на основе ECS архитектуры с поддержкой современных веб-технологий.

## Архитектура

### Основные компоненты

- **VisualElement** - базовый класс для всех UI элементов
- **UIManager** - менеджер UI системы, управляет корневым элементом и рендерингом
- **LayoutEngine** - система расчета позиций и размеров элементов (Flexbox-подобная)
- **StyleSheet** - система стилей с поддержкой CSS-подобного синтаксиса
- **AnimationManager** - система анимаций с различными функциями сглаживания

### Система стилей

UI система поддерживает CSS-подобную систему стилей с:
- Классами стилей
- Псевдоклассами (:hover, :active, :disabled, :focus, :first-child, :last-child)
- Каскадным наследованием стилей
- Вычисляемыми стилями (ResolvedStyle)

## Элементы UI

### Базовые элементы

1. **Button** - интерактивная кнопка с поддержкой событий клика
2. **Label** - текстовый элемент с настраиваемым выравниванием
3. **Panel** - контейнер для группировки элементов
4. **TextInput** - поле ввода текста с поддержкой курсора и клавиатуры
5. **Checkbox** - элемент выбора с визуальной галочкой
6. **Slider** - ползунок для выбора числовых значений
7. **ProgressBar** - индикатор прогресса с настраиваемым отображением
8. **Dropdown** - выпадающий список с множественным выбором
9. **Toast** - всплывающие уведомления с анимациями

### Манипуляторы

- **ClickableManipulator** - обработка кликов мыши
- **HoverEffectManipulator** - обработка наведения мыши
- **FocusManipulator** - управление фокусом элементов

## Система анимаций

Поддерживает различные типы анимаций:
- Fade In/Out (появление/исчезновение)
- Slide In (скольжение)
- Scale (масштабирование)
- Пользовательские анимации

### Функции сглаживания
- Linear
- EaseInQuad, EaseOutQuad, EaseInOutQuad
- EaseInCubic, EaseOutCubic, EaseInOutCubic
- EaseInSine, EaseOutSine, EaseInOutSine

## Использование

### Базовый пример

```csharp
// Создание UI менеджера
var uiManager = new UIManager();

// Создание корневого элемента
var root = new VisualElement("root");

// Создание кнопки
var button = new Button("Click me!");
button.OnClick += () => Console.WriteLine("Button clicked!");
button.AddClass("button");

// Добавление в иерархию
root.AddChild(button);
uiManager.SetRoot(root);

// В игровом цикле
uiManager.Update(deltaTime);
uiManager.Render();
```

### Стилизация

```csharp
// Создание пользовательского стиля
var customStyle = new Style
{
    BackgroundColor = Color.Blue,
    TextColor = Color.White,
    BorderRadius = 10,
    Padding = new Padding(15, 10)
};

// Добавление стиля в StyleSheet
styleSheet.AddClass("custom-button", customStyle);

// Добавление псевдокласса
styleSheet.AddHover("custom-button", new Style 
{ 
    BackgroundColor = Color.DarkBlue 
});

// Применение к элементу
button.AddClass("custom-button");
```

### Анимации

```csharp
// Простые анимации
element.FadeIn(0.5f);
element.SlideIn(new Vector2(0, -50), 0.3f);
element.ScaleIn(0.4f);

// Пользовательская анимация
var animation = new Animation(1.0f, progress =>
{
    element.Position = Vector2.Lerp(startPos, endPos, progress);
})
{
    Easing = EasingFunction.EaseOutCubic
};
element.AddAnimation(animation);
```

### Toast уведомления

```csharp
// Показ уведомлений
uiManager.ShowToast("Success!", ToastType.Success);
uiManager.ShowToast("Warning message", ToastType.Warning, 5.0f);
uiManager.ShowToast("Error occurred", ToastType.Error);
```

## Особенности

- **Производительность**: Оптимизированный рендеринг с кэшированием стилей
- **Гибкость**: Модульная архитектура позволяет легко расширять функциональность
- **Совместимость**: Интеграция с Raylib для рендеринга
- **ECS интеграция**: Полная совместимость с ECS архитектурой движка
- **Отзывчивость**: Поддержка различных разрешений экрана
- **Доступность**: Поддержка клавиатурной навигации и фокуса

## Планы развития

- Поддержка изображений и иконок
- Система тем
- Более сложные layout алгоритмы (Grid)
- Поддержка векторной графики
- Интеграция с системой локализации
- Визуальный редактор UI
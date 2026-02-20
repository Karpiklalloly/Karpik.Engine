# UIToolkit

> UI-фреймворк для игрового интерфейса

## 📋 Обзор

- **Слой**: Client
- **Приоритет**: 1 (после стандартных модулей)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`

## 🎯 Назначение

Предоставляет декларативную систему UI с CSS-подобными стилями.

## 📦 Сервисы

| Тип | Описание |
|-----|----------|
| `UIManager` | Управление UI-элементами |

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| — | — | UI обновляется через UIManager |

## 📁 Структура

```
UIToolkit.Core/
├── UIToolkitInstaller.cs   # Инсталлер модуля
├── UIToolkitModule.cs      # ECS-модуль
├── UIManager.cs            # Менеджер UI
├── UIElement.cs            # Элемент UI
├── StyleSheet.cs           # Стили CSS-like
├── StyleRule.cs            # Правило стиля
└── StyleValue.cs           # Значение стиля
```

## 🔗 Зависимости

- `Graphics.Core` — рендеринг
- `Input` — обработка ввода

## 💡 Использование

```csharp
// Создание элемента
var button = new UIElement("button")
{
    Classes = { "btn", "btn-primary" }
};

// Применение стилей
var style = new StyleSheet();
style.AddRule(".btn", new StyleRule
{
    BackgroundColor = Color.Blue,
    Padding = 10
});
```

## ⚠️ Особенности

- CSS-подобный синтаксис стилей
- Иерархия элементов
- Автоматический layout
- Интеграция с Input для обработки кликов

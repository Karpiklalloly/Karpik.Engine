using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class UILayer
{
    public string Name { get; set; }
    public int ZIndex { get; set; }
    public bool Visible { get; set; } = true;
    public bool Interactive { get; set; } = true;
    public bool BlocksInput { get; set; } = false; // Блокирует ли слой ввод для нижних слоев
    
    private VisualElement _root;
    public VisualElement Root => _root;
    
    // Настройки фона слоя
    public Color? BackgroundColor { get; set; }
    public float BackgroundOpacity { get; set; } = 1.0f;
    
    public UILayer(string name, int zIndex = 0)
    {
        Name = name;
        ZIndex = zIndex;
        
        // Автоматически создаем root элемент для слоя
        _root = new VisualElement();
        _root.Name = $"{name}_Root";
        // Root элемент занимает весь экран по умолчанию
        _root.Style.Width = null; // Будет установлено LayoutEngine
        _root.Style.Height = null; // Будет установлено LayoutEngine
    }
    
    /// <summary>
    /// Добавляет элемент в корень слоя
    /// </summary>
    public void AddElement(VisualElement element)
    {
        _root.AddChild(element);
    }
    
    /// <summary>
    /// Удаляет элемент из корня слоя
    /// </summary>
    public void RemoveElement(VisualElement element)
    {
        _root.RemoveChild(element);
    }
    
    /// <summary>
    /// Очищает все элементы в слое
    /// </summary>
    public void Clear()
    {
        // Правильно очищаем дочерние элементы
        var children = _root.Children.ToList(); // Создаем копию списка
        foreach (var child in children)
        {
            _root.RemoveChild(child);
        }
    }
    
    public void Update(double deltaTime)
    {
        if (!Visible || !Interactive) return;
        Root.Update(deltaTime);
    }
    
    public bool ProcessMouseEvents(Vector2 mousePos)
    {
        if (!Visible || !Interactive) return false;
        
        // Проверяем, попал ли клик в элементы этого слоя
        bool hitElement = Root.ContainsPoint(mousePos);
        
        // Если слой блокирует ввод, всегда возвращаем true (блокируем нижние слои)
        // Если не блокирует, возвращаем true только если попали в элемент
        return BlocksInput || hitElement;
    }
    
    public bool HandleInputEvent(InputEvent inputEvent)
    {
        if (!Visible || !Interactive) return false;
        
        // Передаем событие корневому элементу слоя
        bool eventHandled = Root.HandleInputEvent(inputEvent);
        
        // Если слой блокирует ввод, возвращаем true независимо от того, было ли событие обработано
        // Это предотвращает передачу события нижним слоям
        return eventHandled || BlocksInput;
    }
    
    public void Render(Rectangle screenBounds, StyleSheet? globalStyleSheet = null)
    {
        if (!Visible) return;
        
        // Рендерим фон слоя если задан
        if (BackgroundColor.HasValue)
        {
            var bgColor = BackgroundColor.Value;
            bgColor.A = (byte)(bgColor.A * BackgroundOpacity);
            Raylib.DrawRectangle(0, 0, (int)screenBounds.Width, (int)screenBounds.Height, bgColor);
        }
        
        // Рассчитываем layout и рендерим содержимое слоя
        try
        {
            LayoutEngine.CalculateLayout(Root, globalStyleSheet, screenBounds);
            Root.Render();
        }
        catch (Exception e)
        {
            // Игнорируем ошибки рендеринга
        }
    }
    
    public bool HandleInput(Vector2 mousePos)
    {
        if (!Visible || !Interactive) return false;
        
        // Проверяем, попадает ли ввод в элементы этого слоя
        bool inputHandled = Root.ContainsPoint(mousePos);
        
        // Если слой блокирует ввод, возвращаем true независимо от того, попали ли в элементы
        // Это предотвращает обработку ввода нижними слоями
        // НО: если клик попал в наши элементы, мы все равно должны их обработать
        return inputHandled || BlocksInput;
    }
}

public class LayerManager
{
    private readonly List<UILayer> _layers = new();
    private readonly Dictionary<string, UILayer> _layersByName = new();
    
    public IReadOnlyList<UILayer> Layers => _layers.AsReadOnly();
    
    public UILayer CreateLayer(string name, int zIndex = 0)
    {
        if (_layersByName.ContainsKey(name))
        {
            throw new ArgumentException($"Layer with name '{name}' already exists");
        }
        
        var layer = new UILayer(name, zIndex);
        AddLayer(layer);
        return layer;
    }
    
    public void AddLayer(UILayer layer)
    {
        if (_layersByName.ContainsKey(layer.Name))
        {
            throw new ArgumentException($"Layer with name '{layer.Name}' already exists");
        }
        
        _layers.Add(layer);
        _layersByName[layer.Name] = layer;
        
        // Сортируем слои по Z-индексу
        _layers.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
    }
    
    public void RemoveLayer(string name)
    {
        if (_layersByName.TryGetValue(name, out var layer))
        {
            _layers.Remove(layer);
            _layersByName.Remove(name);
        }
    }
    
    public UILayer? GetLayer(string name)
    {
        return _layersByName.TryGetValue(name, out var layer) ? layer : null;
    }
    
    public void SetLayerZIndex(string name, int zIndex)
    {
        if (_layersByName.TryGetValue(name, out var layer))
        {
            layer.ZIndex = zIndex;
            // Пересортировываем слои
            _layers.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
        }
    }
    
    public void ShowLayer(string name)
    {
        if (_layersByName.TryGetValue(name, out var layer))
        {
            layer.Visible = true;
        }
    }
    
    public void HideLayer(string name)
    {
        if (_layersByName.TryGetValue(name, out var layer))
        {
            layer.Visible = false;
        }
    }
    
    public void Update(double deltaTime)
    {
        // Обновляем слои в обратном порядке (сверху вниз) для правильной обработки ввода
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            _layers[i].Update(deltaTime);
        }
    }
    
    public void Render(Rectangle screenBounds, StyleSheet? globalStyleSheet = null)
    {
        // Рендерим слои в прямом порядке (снизу вверх)
        foreach (var layer in _layers)
        {
            layer.Render(screenBounds, globalStyleSheet);
        }
    }
    
    public bool HandleInput(Vector2 mousePos)
    {
        // Обрабатываем ввод в обратном порядке (сверху вниз)
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.ProcessMouseEvents(mousePos))
            {
                return true; // Ввод обработан, прекращаем обработку нижних слоев
            }
        }
        
        return false;
    }
    
    public void Clear()
    {
        _layers.Clear();
        _layersByName.Clear();
    }
}
using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Modal : VisualElement
{
    public string Title { get; set; } = "";
    public bool ShowCloseButton { get; set; } = true;
    public bool CloseOnBackgroundClick { get; set; } = true;
    public Vector2 MinSize { get; set; } = new(300, 200);
    public Vector2 MaxSize { get; set; } = new(800, 600);
    
    private Button? _closeButton;
    private VisualElement? _titleBar;
    private VisualElement? _contentArea;
    private bool _isDragging = false;
    private Vector2 _dragOffset;
    
    public event Action? OnClose;
    
    public Modal(string title = "Modal") : base("Modal")
    {
        Title = title;
        AddClass("modal");
        
        // Устанавливаем размер по умолчанию
        Size = new Vector2(400, 300);
        
        // Устанавливаем вертикальную компоновку
        Style.FlexDirection = FlexDirection.Column;
        
        CreateModalStructure();
    }
    
    private void CreateModalStructure()
    {
        // Создаем заголовок
        _titleBar = new VisualElement("TitleBar");
        _titleBar.AddClass("modal-title-bar");
        _titleBar.Style.FlexDirection = FlexDirection.Row;
        AddChild(_titleBar);
        
        var titleLabel = new Label(Title);
        titleLabel.AddClass("modal-title");
        _titleBar.AddChild(titleLabel);
        
        // Кнопка закрытия
        if (ShowCloseButton)
        {
            _closeButton = new Button("×");
            _closeButton.AddClass("modal-close-button");
            _closeButton.OnClick += () => Close();
            _titleBar.AddChild(_closeButton);
        }
        
        // Область контента
        _contentArea = new VisualElement("ContentArea");
        _contentArea.AddClass("modal-content");
        _contentArea.Style.FlexDirection = FlexDirection.Column;
        _contentArea.Style.FlexGrow = 1;
        AddChild(_contentArea);
        
        // Добавляем возможность перетаскивания за заголовок
        var dragManipulator = new DragManipulator();
        _titleBar.AddManipulator(dragManipulator);
    }
    
    public void SetContent(VisualElement content)
    {
        _contentArea?.Children.Clear();
        _contentArea?.AddChild(content);
    }
    
    public void AddContent(VisualElement content)
    {
        _contentArea?.AddChild(content);
    }
    
    public void Close()
    {
        OnClose?.Invoke();
    }
    
    public void CenterOnScreen(Vector2 screenSize)
    {
        Position = new Vector2(
            (screenSize.X - Size.X) / 2,
            (screenSize.Y - Size.Y) / 2
        );
    }
    
    protected override void RenderSelf()
    {
        // Рендерим тень модального окна
        var shadowOffset = new Vector2(4, 4);
        var shadowColor = new Color(0, 0, 0, 100);
        Raylib.DrawRectangle(
            (int)(Position.X + shadowOffset.X), 
            (int)(Position.Y + shadowOffset.Y),
            (int)Size.X, (int)Size.Y, 
            shadowColor
        );
        
        // Рендерим основной фон
        base.RenderSelf();
    }
}

// Менеджер модальных окон
public class ModalManager
{
    private readonly LayerManager _layerManager;
    private readonly List<Modal> _activeModals = new();
    private int _nextZIndex = 1000; // Начинаем с высокого Z-индекса для модальных окон
    
    public ModalManager(LayerManager layerManager)
    {
        _layerManager = layerManager;
    }
    
    public void ShowModal(Modal modal, bool blockBackground = true)
    {
        var layerName = $"modal_{modal.Name}_{_nextZIndex}";
        var layer = _layerManager.CreateLayer(layerName, _nextZIndex++);
        
        // Настраиваем слой
        layer.AddElement(modal);
        layer.BlocksInput = true; // Блокируем ввод для нижних слоев, но не для элементов модального окна
        
        // Добавляем полупрозрачный фон если нужно
        if (blockBackground)
        {
            layer.BackgroundColor = new Color(0, 0, 0, 128);
        }
        
        // Центрируем модальное окно
        var screenSize = new Vector2(Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        modal.CenterOnScreen(screenSize);
        
        // Добавляем в список активных модальных окон
        _activeModals.Add(modal);
        
        // Подписываемся на закрытие
        modal.OnClose += () => CloseModal(modal, layerName);
        
        // Анимация появления
        modal.ScaleIn(0.3f);
    }
    
    public void CloseModal(Modal modal, string? layerName = null)
    {
        _activeModals.Remove(modal);
        
        // Анимация исчезновения
        modal.FadeOut(0.2f, () =>
        {
            // Удаляем слой после анимации
            if (layerName != null)
            {
                _layerManager.RemoveLayer(layerName);
            }
            else
            {
                // Ищем слой по модальному окну
                var layer = _layerManager.Layers.FirstOrDefault(l => l.Root.Children.Contains(modal));
                if (layer != null)
                {
                    _layerManager.RemoveLayer(layer.Name);
                }
            }
        });
    }
    
    public void CloseAllModals()
    {
        var modalsToClose = _activeModals.ToList();
        foreach (var modal in modalsToClose)
        {
            modal.Close();
        }
    }
    
    public bool HasActiveModals => _activeModals.Count > 0;
}
using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Elements;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class UIManager
{
    public VisualElement? Root { get; set; }
    public StyleSheet StyleSheet { get; set; }
    public LayerManager LayerManager { get; private set; }
    public InputManager InputManager { get; private set; }
    public ToastManager? ToastManager { get; private set; }
    public ModalManager? ModalManager { get; private set; }
    public ContextMenuManager? ContextMenuManager { get; private set; }
    public TooltipManager? TooltipManager { get; private set; }
    
    public UIManager()
    {
        StyleSheet = StyleSheet.CreateDefault();
        LayerManager = new LayerManager();
        InputManager = new InputManager(LayerManager);
    }
    
    public void Update(float deltaTime)
    {
        // Обновляем систему ввода
        InputManager.Update();
        
        // // Обновляем основной UI
        // Root?.Update(deltaTime);
        
        // Обновляем слои
        LayerManager.Update(deltaTime);
    }
    
    public void Render()
    {
        var screenSize = new Rectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        
        // Рендерим основной UI
        if (Root != null)
        {
            try
            {
                LayoutEngine.CalculateLayout(Root, StyleSheet, screenSize);
                Root.Render();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        
        // Рендерим слои поверх основного UI
        try
        {
            LayerManager.Render(screenSize, StyleSheet);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public void SetRoot(VisualElement root)
    {
        Root = root;
        
        // Создаем основной слой для главного UI
        var mainLayer = LayerManager.CreateLayer("main", 0);
        mainLayer.AddElement(root);
        
        // Инициализируем менеджеры
        ToastManager = new ToastManager(root);
        ModalManager = new ModalManager(LayerManager);
        ContextMenuManager = new ContextMenuManager(LayerManager);
        TooltipManager = new TooltipManager(LayerManager);
        
        // Устанавливаем статическую ссылку
        UIManagerInstance.Current = this;
    }
    
    public void ShowToast(string message, ToastType type = ToastType.Info, float duration = 3f)
    {
        ToastManager?.ShowToast(message, type, duration);
    }
    
    public void ShowModal(Modal modal, bool blockBackground = true)
    {
        ModalManager?.ShowModal(modal, blockBackground);
    }
    
    public void ShowContextMenu(ContextMenu menu, Vector2 position)
    {
        ContextMenuManager?.ShowContextMenu(menu, position);
    }
    
    public bool HandleInput(Vector2 mousePos)
    {
        // Сначала проверяем слои (они имеют приоритет)
        if (LayerManager.HandleInput(mousePos))
        {
            return true;
        }
        
        // Затем основной UI
        return Root?.ContainsPoint(mousePos) ?? false;
    }
}

// Статический доступ к текущему UIManager
public static class UIManagerInstance
{
    public static UIManager? Current { get; set; }
}
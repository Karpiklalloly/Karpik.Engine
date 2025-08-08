using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Elements;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class UIManager
{
    public VisualElement? Root { get; set; }
    public StyleSheet StyleSheet { get; set; }
    public ToastManager? ToastManager { get; private set; }
    
    public UIManager()
    {
        StyleSheet = StyleSheet.CreateDefault();
    }
    
    public void Update(float deltaTime)
    {
        Root?.Update(deltaTime);
    }
    
    public void Render()
    {
        if (Root == null) return;
        
        // Рассчитываем layout
        var screenSize = new Rectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        try
        {
            LayoutEngine.CalculateLayout(Root, StyleSheet, screenSize);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        try
        {
            // Рендерим UI
            Root.Render();
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
        ToastManager = new ToastManager(root);
    }
    
    public void ShowToast(string message, ToastType type = ToastType.Info, float duration = 3f)
    {
        ToastManager?.ShowToast(message, type, duration);
    }
}
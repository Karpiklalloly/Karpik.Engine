using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Core;

public class UIManager
{
    public VisualElement? Root { get; set; }
    public StyleSheet StyleSheet { get; set; }
    
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
        LayoutEngine.CalculateLayout(Root, StyleSheet, screenSize);
        
        // Рендерим UI
        Root.Render();
    }
    
    public void SetRoot(VisualElement root)
    {
        Root = root;
    }
}
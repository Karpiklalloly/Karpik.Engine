using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class Label : VisualElement
{
    public string Text
    {
        get => _text;
        set => SetText(value);
    }

    public Font Font { get; set; } = UI.DefaultFont;

    public Color Color { get; set; } = Color.White;
    public Vector2 TextAlignment { get; set; } = new Vector2(0.0f, 0.5f); // Выравнивание текста внутри Bounds 
    public float FontSize { get; set; } = 64f;

    private string _text = string.Empty;
    
    public Label(string text) : base(Vector2.Zero)
    {
        SetText(text);
    }
    
    // Обновляем размер, если текст изменился
    public void SetText(string text)
    {
        if (_text == text) return;
        
        _text = text;
        if (string.IsNullOrEmpty(Text)) return;
        
        Size = Raylib.MeasureTextEx(Font, Text, FontSize, 0);
        UpdateLayout();
    }
    
    protected override void DrawSelf()
    {
        if (string.IsNullOrEmpty(Text) || !Raylib.IsFontValid(Font)) return;

        var textPos = Utils.GetTextPosition(Bounds, Size, TextAlignment);
        Raylib.DrawTextEx(Font, Text, textPos, FontSize, 0, Color);
    }
}
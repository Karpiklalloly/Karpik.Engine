namespace Karpik.Engine.Client.UI.Core;

public interface IRenderer
{
    void DrawRect(Rectangle rect, Color background);
    void DrawRectOutline(Rectangle rect, Color outline, float thickness = 1f);
    void DrawText(Rectangle rect, string text, Color color, float fontSize, TextAlignment alignment = TextAlignment.Left);
    void DrawTexture(Rectangle rect, TextureId texture, Color tint);
    void DrawRoundedRect(Rectangle rect, Color background, float cornerRadius);
    void DrawGradient(Rectangle rect, Color startColor, Color endColor, bool horizontal);
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public readonly struct TextureId
{
    public readonly int Id;
    public TextureId(int id) => Id = id;
    public static TextureId Empty => new(0);
    public bool IsValid => Id != 0;
}

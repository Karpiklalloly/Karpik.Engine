using System.Numerics;

namespace Karpik.Engine.Client;

// UIAnchor теперь может представлять одну точку или область
public struct Anchor
{
    public Vector2 Min { get; set; } // 0.0 to 1.0
    public Vector2 Max { get; set; } // 0.0 to 1.0

    // True если якоря определяют область, а не одну точку
    public bool StretchesHorizontally => Math.Abs(Min.X - Max.X) > 0.001f;
    public bool StretchesVertically => Math.Abs(Min.Y - Max.Y) > 0.001f;

    // Статические пресеты
    public static readonly Anchor TopLeft = new Anchor(0f, 0f);
    public static readonly Anchor TopCenter = new Anchor(0.5f, 0f);
    public static readonly Anchor TopRight = new Anchor(1f, 0f);
    public static readonly Anchor MiddleLeft = new Anchor(0f, 0.5f);
    public static readonly Anchor Center = new Anchor(0.5f, 0.5f);
    public static readonly Anchor MiddleRight = new Anchor(1f, 0.5f);
    public static readonly Anchor BottomLeft = new Anchor(0f, 1f);
    public static readonly Anchor BottomCenter = new Anchor(0.5f, 1f);
    public static readonly Anchor BottomRight = new Anchor(1f, 1f);
    public static readonly Anchor StretchAll = new Anchor(0f, 0f, 1f, 1f);

    // Конструкторы
    public Anchor(float x, float y) : this(x, y, x, y) { } // Точка
    public Anchor(Vector2 point) : this(point.X, point.Y, point.X, point.Y) { } // Точка
    public Anchor(float minX, float minY, float maxX, float maxY) { Min = new Vector2(minX, minY); Max = new Vector2(maxX, maxY); }
    public Anchor(Vector2 min, Vector2 max) { Min = min; Max = max; }
}
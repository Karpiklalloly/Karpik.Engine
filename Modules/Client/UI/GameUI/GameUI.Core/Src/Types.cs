namespace Karpik.Engine.Client.UI.Core;
using Vector2 = System.Numerics.Vector2;

public struct Rectangle
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public Rectangle(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float Left => X;
    public float Right => X + Width;
    public float Top => Y;
    public float Bottom => Y + Height;

    public Vector2 Position
    {
        get => new Vector2(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    public Vector2 Size
    {
        get => new Vector2(Width, Height);
        set
        {
            Width = value.X;
            Height = value.Y;
        }
    }

    public Vector2 Center => new Vector2(X + Width / 2, Y + Height / 2);

    public bool Contains(Vector2 point)
    {
        return point.X >= X && point.X <= X + Width &&
               point.Y >= Y && point.Y <= Y + Height;
    }

    public bool Intersects(Rectangle other)
    {
        return other.Left < Right &&
               other.Right > Left &&
               other.Top < Bottom &&
               other.Bottom > Top;
    }

    public Rectangle Inflate(float horizontal, float vertical)
    {
        return new Rectangle(X - horizontal, Y - vertical, 
            Width + horizontal * 2, Height + vertical * 2);
    }

    public Rectangle Inflate(float all)
    {
        return Inflate(all, all);
    }

    public override string ToString() => $"Rectangle({X}, {Y}, {Width}x{Height})";

    public static Rectangle Zero => new(0, 0, 0, 0);
    public static Rectangle Empty => Zero;
}

public struct Color
{
    public byte A;
    public byte R;
    public byte G;
    public byte B;

    public Color(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    public Color(byte r, byte g, byte b) : this(255, r, g, b) { }

    public static Color FromArgb(int argb)
    {
        return new Color(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF)
        );
    }

    public static Color FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex string cannot be empty", nameof(hex));
            
        hex = hex.TrimStart('#');
        
        try
        {
            if (hex.Length == 6)
            {
                return new Color(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            if (hex.Length == 8)
            {
                return new Color(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16)
                );
            }
            throw new ArgumentException("Invalid hex color format", nameof(hex));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid hex color format", nameof(hex));
        }
    }

    public int ToArgb() => (A << 24) | (R << 16) | (G << 8) | B;

    public static Color White => new(255, 255, 255);
    public static Color Black => new(0, 0, 0);
    public static Color Gray => new(128, 128, 128);
    public static Color Red => new(255, 0, 0);
    public static Color Green => new(0, 255, 0);
    public static Color Blue => new(0, 0, 255);
    public static Color Yellow => new(255, 255, 0);
    public static Color Cyan => new(0, 255, 255);
    public static Color Magenta => new(255, 0, 255);
    public static Color Transparent => new(0, 0, 0, 0);

    public override string ToString() => $"Color(A={A}, R={R}, G={G}, B={B})";

    public static bool operator ==(Color a, Color b) => a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
    public static bool operator !=(Color a, Color b) => !(a == b);
    public override bool Equals(object? obj) => obj is Color c && this == c;
    public override int GetHashCode() => HashCode.Combine(A, R, G, B);
}

public struct Size
{
    public float Width;
    public float Height;

    public Size(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public Size(float both) : this(both, both) { }

    public static Size Zero => new(0, 0);
    public static Size Auto => new(-1, -1);

    public bool IsAuto => Width < 0 || Height < 0;

    public override string ToString() => $"Size({Width}x{Height})";
}

public struct Padding
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

    public Padding(float all)
    {
        Left = Top = Right = Bottom = all;
    }

    public Padding(float horizontal, float vertical)
    {
        Left = Right = horizontal;
        Top = Bottom = vertical;
    }

    public Padding(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static Padding Zero => new(0);

    public static bool operator ==(Padding a, Padding b) => 
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    public static bool operator !=(Padding a, Padding b) => !(a == b);
    public override bool Equals(object? obj) => obj is Padding p && this == p;
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
}

public struct Margin
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

    public Margin(float all)
    {
        Left = Top = Right = Bottom = all;
    }

    public Margin(float horizontal, float vertical)
    {
        Left = Right = horizontal;
        Top = Bottom = vertical;
    }

    public Margin(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static Margin Zero => new(0);

    public static bool operator ==(Margin a, Margin b) => 
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    public static bool operator !=(Margin a, Margin b) => !(a == b);
    public override bool Equals(object? obj) => obj is Margin m && this == m;
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
}
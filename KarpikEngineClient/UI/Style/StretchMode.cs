namespace Karpik.Engine.Client;

[Flags]
public enum StretchMode
{
    None = 0,
    Horizontal = 1, // Растягивать по горизонтали
    Vertical = 2,   // Растягивать по вертикали
    Both = Horizontal | Vertical // Растягивать в обе стороны
}
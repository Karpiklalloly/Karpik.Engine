namespace Karpik.Engine.Client.UI.Core;

public struct UIWidget
{
    public UiTypeId Type;
    public string Id;
    public Rectangle Bounds;
    public int ZIndex;

    public int ParentIndex;
    public int FirstChildIndex;
    public int NextSiblingIndex;
    public int PrevSiblingIndex;

    public InteractionState State;
    public bool IsVisible;
    public bool IsEnabled;
    public bool BubbleEvents;
    public bool IsDirty;

    public const int NoParent = -1;
    public const int NoChild = -1;
    public const int NoSibling = -1;

    public UIWidget(UiTypeId type)
    {
        Type = type;
        Id = string.Empty;
        Bounds = Rectangle.Zero;
        ZIndex = 0;
        ParentIndex = NoParent;
        FirstChildIndex = NoChild;
        NextSiblingIndex = NoSibling;
        PrevSiblingIndex = NoSibling;
        State = InteractionState.Normal;
        IsVisible = true;
        IsEnabled = true;
        BubbleEvents = false;
        IsDirty = true;
    }

    public bool HasParent => ParentIndex != NoParent;
    public bool HasChildren => FirstChildIndex != NoChild;
    public bool HasNextSibling => NextSiblingIndex != NoSibling;
    public bool HasPrevSibling => PrevSiblingIndex != NoSibling;

    public void SetPosition(float x, float y)
    {
        Bounds.X = x;
        Bounds.Y = y;
    }

    public void SetSize(float width, float height)
    {
        Bounds.Width = width;
        Bounds.Height = height;
    }
}

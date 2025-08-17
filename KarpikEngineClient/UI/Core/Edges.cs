namespace Karpik.Engine.Client.UIToolkit;

public struct Edges
{
    public StyleValue Top;
    public StyleValue Right;
    public StyleValue Bottom;
    public StyleValue Left;
    public Edges() { Top = Right = Bottom = Left = StyleValue.Px(0); }
}
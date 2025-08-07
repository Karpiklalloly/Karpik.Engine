using System.Numerics;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public static class UI
{
    public static VisualElement Root { get; internal set; }
    public static Font DefaultFont { get; internal set; }

    internal static void Update()
    {
        Root.Style.Width = Raylib.GetRenderWidth() - 200;
        Root.Style.Height = Raylib.GetRenderHeight() - 200;
        var computed = Root.GetComputedStyle();
        Root.Position = new Vector2(Raylib.GetRenderWidth() / 2 - computed.Width / 2, Raylib.GetRenderHeight() / 2 - computed.Height / 2);
        Root.Update(Time.DeltaTime);
    }

    internal static void Draw()
    {
        Root.Render();
    }
}
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
        Root.Style.Width = Raylib.GetRenderWidth();
        Root.Style.Height = Raylib.GetRenderHeight();
        Root.Update(Time.DeltaTime);
    }

    internal static void Draw()
    {
        Root.Render();
    }
}
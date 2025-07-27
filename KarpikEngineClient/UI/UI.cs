using System.Numerics;
using Karpik.Engine.Client.VisualElements;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public static class UI
{
    public static VisualElement Root { get; internal set; }
    public static Font DefaultFont { get; internal set; }

    internal static void Update()
    {
        Root.Size = new Vector2(Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        Root.Update(Time.DeltaTime);
    }

    internal static void Draw()
    {
        var elements = Root.AllChildren.Concat([Root]).OrderBy(x => x.Order);
        
        foreach (var element in elements)
        {
            element.Draw(Time.DeltaTime);
        }
        Root.Draw(Time.DeltaTime);
    }
}
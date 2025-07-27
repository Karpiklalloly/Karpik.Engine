using System.Numerics;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public static class Drawer
{
    private static DrawAction[] _actions = new DrawAction[128];
    private static int _actionsCount = 0;
    
    public static void Sprite(SpriteRenderer spriteRenderer, Position position, Rotation rotation, Scale scale)
    {
        ResizeIfNeed();
        _actions[_actionsCount++] = new DrawAction()
        {
            Texture = spriteRenderer.Texture,
            Position = position.Value,
            Color = spriteRenderer.Color,
            Rotation = rotation.Value,
            Scale = scale.Value,
            Layer = spriteRenderer.Layer
        };
    }

    internal static void Draw()
    {
        Array.Sort(_actions, (a, b) => a.Layer - b.Layer);
        while (_actionsCount > 0)
        {
            _actions[--_actionsCount].Draw();
        }
    }

    private static void ResizeIfNeed()
    {
        if (_actionsCount >= _actions.Length)
        {
            Array.Resize(ref _actions, _actions.Length * 2);
        }
    }

    private struct DrawAction
    {
        public Texture2D Texture;
        public Vector<double> Position;
        public Color Color;
        public double Rotation;
        public double Scale;
        public int Layer;
        
        public void Draw() => Raylib.DrawTextureEx(Texture, new Vector2((float)Position[0], (float)Position[1]), (float)Rotation, (float)Scale, Color);
    }
}
using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.MyGame.Shared.Main;

namespace Karpik.Engine.MyGame.Client.Main;

public class Drawer
{
    private DrawAction[] _actions = new DrawAction[128];
    private int _actionsCount = 0;
    [DI] private IRenderer _renderer = null!;
    
    public void Sprite(SpriteRenderer spriteRenderer, Position position, Rotation rotation, Scale scale)
    {
        ResizeIfNeed();
        _actions[_actionsCount++] = new DrawAction()
        {
            Texture = spriteRenderer.Texture,
            Position = new Vector3(position.X, position.Y, position.Z),
            Color = spriteRenderer.Color,
            Rotation = rotation.Value,
            Scale = scale.Value,
            Layer = spriteRenderer.Layer
        };
    }

    internal void Draw()
    {
        Array.Sort(_actions, static (a, b) => a.Layer - b.Layer);
        while (_actionsCount > 0)
        {
            _actions[--_actionsCount].Draw(_renderer);
        }
    }

    private void ResizeIfNeed()
    {
        if (_actionsCount >= _actions.Length)
        {
            Array.Resize(ref _actions, _actions.Length * 2);
        }
    }

    private struct DrawAction
    {
        public ITexture2D Texture;
        public Vector3 Position;
        public Color Color;
        public double Rotation;
        public double Scale;
        public int Layer;
        
        public void Draw(IRenderer renderer) => renderer.DrawTexture(Texture, new Vector2((float)Position[0], (float)Position[1]), (float)Rotation, (float)Scale, Color);
    }
}
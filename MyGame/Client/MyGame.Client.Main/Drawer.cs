using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Client.Main;

[Karpik.Engine.Shared.ECS.Scheduling.RenderPrepareCommand]
public class Drawer
{
    private SpriteAction[] _actions = new SpriteAction[128];
    private int _actionsCount = 0;
    // [DI] private IRenderer2D _renderer = null!;
    // [DI] private ICamera2D _camera2D = null!;
    public void Sprite(SpriteRenderer spriteRenderer, Transform2D transform)
    {
        EnsureCapacity();
        _actions[_actionsCount++] = new SpriteAction()
        {
            Texture = spriteRenderer.Texture,
            Position = transform.Position,
            Color = spriteRenderer.Color,
            Rotation = transform.Rotation,
            Layer = spriteRenderer.Layer,
            Size = new Vector2(spriteRenderer.Width, spriteRenderer.Height)
        };
    }

    internal void Draw()
    {
        while (_actionsCount > 0)
        {
            _actions[--_actionsCount].Draw();
        }
    }

    private void EnsureCapacity()
    {
        if (_actionsCount >= _actions.Length)
        {
            throw new InvalidOperationException(
                $"Drawer sprite action capacity exceeded. Current capacity: {_actions.Length}. Increase warm-up capacity before render preparation.");
        }
    }

    private struct SpriteAction
    {
        public ITexture2D? Texture;
        public Vector2 Position;
        public Vector2 Size;
        public Color Color;
        public double Rotation;
        public int Layer;
        
        public void Draw()
        {
            if (Texture is not null)
            {
                GraphicsContext.Buffer.AddTextureCentered(
                    Texture,
                    Position,
                    Size,
                    Color,
                    (float)Rotation,
                    DrawSpace.World,
                    sortKey: DrawSortKey.FromLayerDescending(Layer));
            }
        }
    }
}

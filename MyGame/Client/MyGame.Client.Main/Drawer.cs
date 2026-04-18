using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Client.Main;

public class Drawer
{
    private SpriteAction[] _actions = new SpriteAction[128];
    private int _actionsCount = 0;
    // [DI] private IRenderer2D _renderer = null!;
    // [DI] private ICamera2D _camera2D = null!;
    [DI] private Application _application = null!;

    public void Sprite(SpriteRenderer spriteRenderer, Transform2D transform)
    {
        ResizeIfNeed();
        _actions[_actionsCount++] = new SpriteAction()
        {
            // Texture = spriteRenderer.Texture,
            Position = transform.Position,
            Color = spriteRenderer.Color,
            Rotation = transform.Rotation,
            Layer = spriteRenderer.Layer,
            Size = new Vector2(spriteRenderer.Width, spriteRenderer.Height)
        };
    }

    internal void Draw()
    {
        // _renderer.BeginMode2D(_camera2D);
        //
        // var span = _actions.AsSpan(0, _actionsCount);
        // span.Sort(static (a, b) => a.Layer.CompareTo(b.Layer));
        // while (_actionsCount > 0)
        // {
        //     span[--_actionsCount].Draw(_renderer);
        // }
        //
        // _renderer.End2DMode();
    }

    private void ResizeIfNeed()
    {
        if (_actionsCount >= _actions.Length)
        {
            Array.Resize(ref _actions, _actions.Length * 2);
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
        
        // public void Draw(IRenderer2D renderer)
        // {
        //     if (Texture is not null)
        //     {
        //         RectangleF sourceRec = new RectangleF(0, 0, Texture.Width, Texture.Height);
        //         RectangleF destRec = new RectangleF(
        //             Position.X,
        //             -Position.Y,
        //             Size.X,
        //             Size.Y
        //         );
        //         Vector2 origin = new Vector2(Size.X / 2f, Size.Y / 2f);
        //         
        //         renderer.DrawTexture(Texture, sourceRec, destRec, origin, (float)Rotation, Color);
        //         renderer.DrawText(
        //             renderer.GetFontDefault(),
        //             $"{Position}",
        //             Position with{Y = -Position.Y - 1},
        //             origin,
        //             (float)Rotation,
        //             1,
        //             0.5f,
        //             Color.Red);
        //     }
        // }
    }
}

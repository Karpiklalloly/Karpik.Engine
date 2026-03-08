using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Client.Main;

public class Drawer
{
    private DrawAction[] _actions = new DrawAction[128];
    private int _actionsCount = 0;
    [DI] private IRenderer _renderer = null!;
    [DI] private ICamera2D _camera2D = null!;
    [DI] private Application _application = null!;

    public void Sprite(SpriteRenderer spriteRenderer, Transform2D position, Rotation rotation)
    {
        ResizeIfNeed();
        
        // Store WORLD coordinates - camera will handle conversion
        _actions[_actionsCount++] = new DrawAction()
        {
            Texture = spriteRenderer.Texture,
            Position = position.Position,  // World coordinates!
            Color = spriteRenderer.Color,
            Rotation = rotation.Value,
            Layer = spriteRenderer.Layer,
            Size = new Vector2(spriteRenderer.Width, spriteRenderer.Height)
        };
    }

    internal void Draw()
    {
        // Begin 2D mode - Raylib will transform coordinates
        _renderer.BeginMode2D(_camera2D);
        
        Array.Sort(_actions, static (a, b) => a.Layer - b.Layer);
        while (_actionsCount > 0)
        {
            _actions[--_actionsCount].Draw(_renderer);
        }
        
        // End 2D mode
        _renderer.End2DMode();
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
        public Vector2 Position;  // World coordinates now!
        public Vector2 Size;
        public Color Color;
        public double Rotation;
        public int Layer;
        
        public void Draw(IRenderer renderer)
        {
            if (Texture != null)
            {
                RectangleF sourceRec = new RectangleF(0, 0, Texture.Width, Texture.Height);
                RectangleF destRec = new RectangleF(
                    Position.X,
                    -Position.Y, // ИНВЕРСИЯ Y для Raylib
                    Size.X,
                    Size.Y
                );
                Vector2 origin = new Vector2(Size.X / 2f, Size.Y / 2f);
                
                renderer.DrawTexture(Texture, sourceRec, destRec, origin, (float)Rotation, Color);
            }
            else
            {
                // Fallback: draw a colored rectangle when texture is missing
                // Note: Position is center, so we offset by half size
                var rect = new RectangleF(Position.X - 32, Position.Y - 32, 64, 64);
                renderer.DrawRectangle(rect, Color);
            }
        }
    }
}

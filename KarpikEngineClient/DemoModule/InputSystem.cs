using System.Numerics;
using Game.Generated.Client;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Network;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class InputSystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    class Aspect : EcsAspect
    {
        public EcsPool<LocalPlayer> player = Inc;
        public EcsPool<Position> position = Inc;
        public EcsPool<NetworkId> networkId = Inc;
    }
    
    private EcsDefaultWorld _world;
    private const float SPACE_SPEED_MULTIPLIER = 5f;
    
    public void Run()
    {
        if (Input.IsMouseRightButtonDown)
        {
            Raylib.DisableCursor();
            return;
        }
        
        if (Input.IsMouseRightButtonUp)
        {
            Raylib.EnableCursor();
            return;
        }

        if (Input.IsMouseLeftButtonHold)
        {
            Vector3 currentInput = Vector3.Zero;

            if (Input.IsDown(KeyboardKey.A) || Input.IsDown(KeyboardKey.Left))
            {
                currentInput.Y -= 1;
            }

            if (Input.IsDown(KeyboardKey.D) || Input.IsDown(KeyboardKey.Right))
            {
                currentInput.Y += 1;
            }

            if (Input.IsDown(KeyboardKey.W) || Input.IsDown(KeyboardKey.Up))
            {
                currentInput.X += 1;
            }

            if (Input.IsDown(KeyboardKey.S) || Input.IsDown(KeyboardKey.Down))
            {
                currentInput.X -= 1;
            }
            
            if (Input.IsDown(KeyboardKey.Q))
            {
                currentInput.Z += 1;
            }

            if (Input.IsDown(KeyboardKey.E))
            {
                currentInput.Z -= 1;
            }
            
            if (Input.IsPressing(KeyboardKey.Space))
            {
                currentInput *= SPACE_SPEED_MULTIPLIER;
            }

            var span = _world.Where(out Aspect a);
            foreach (var e in span)
            {
                Rpc.Instance.Move(new MoveCommand()
                {
                    Source = -1,
                    Target = a.networkId.Get(e).Id,
                    Direction = currentInput,
                });
            }
        }
        
        if (Raylib.IsCursorHidden())
        {
            Vector3 currentInput = Vector3.Zero;

            if (Input.IsDown(KeyboardKey.A) || Input.IsDown(KeyboardKey.Left))
            {
                currentInput.Y -= 1;
            }

            if (Input.IsDown(KeyboardKey.D) || Input.IsDown(KeyboardKey.Right))
            {
                currentInput.Y += 1;
            }

            if (Input.IsDown(KeyboardKey.W) || Input.IsDown(KeyboardKey.Up))
            {
                currentInput.X += 1;
            }

            if (Input.IsDown(KeyboardKey.S) || Input.IsDown(KeyboardKey.Down))
            {
                currentInput.X -= 1;
            }

            if (Input.IsDown(KeyboardKey.Q))
            {
                currentInput.Z += 1;
            }

            if (Input.IsDown(KeyboardKey.E))
            {
                currentInput.Z -= 1;
            }

            if (Input.IsPressing(KeyboardKey.Space))
            {
                currentInput *= SPACE_SPEED_MULTIPLIER;
            }

            Camera.Main.Rotate(Input.MouseDelta * (float)Time.DeltaTime / 2);
            Camera.Main.Move(currentInput * (float)Time.DeltaTime * 2);
        }
        
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}
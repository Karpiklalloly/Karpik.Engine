using System.Numerics;
using Game.Generated.Client;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Network;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class InputSystem : IEcsRun
{
    class Aspect : EcsAspect
    {
        public EcsPool<LocalPlayer> player = Inc;
        public EcsPool<Position> position = Inc;
        public EcsPool<NetworkId> networkId = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    [DI] private Rpc _rpc;
    [DI] private Input _input;
    private const float SPACE_SPEED_MULTIPLIER = 5f;
    
    public void Run()
    {
        if (_input.IsMouseRightButtonDown)
        {
            Raylib.DisableCursor();
            return;
        }
        
        if (_input.IsMouseRightButtonUp)
        {
            Raylib.EnableCursor();
            return;
        }

        if (_input.IsMouseLeftButtonHold)
        {
            Vector3 currentInput = Vector3.Zero;

            if (_input.IsDown(KeyboardKey.A) || _input.IsDown(KeyboardKey.Left))
            {
                currentInput.Y -= 1;
            }

            if (_input.IsDown(KeyboardKey.D) || _input.IsDown(KeyboardKey.Right))
            {
                currentInput.Y += 1;
            }

            if (_input.IsDown(KeyboardKey.W) || _input.IsDown(KeyboardKey.Up))
            {
                currentInput.X += 1;
            }

            if (_input.IsDown(KeyboardKey.S) || _input.IsDown(KeyboardKey.Down))
            {
                currentInput.X -= 1;
            }
            
            if (_input.IsDown(KeyboardKey.Q))
            {
                currentInput.Z += 1;
            }

            if (_input.IsDown(KeyboardKey.E))
            {
                currentInput.Z -= 1;
            }
            
            if (_input.IsPressing(KeyboardKey.Space))
            {
                currentInput *= SPACE_SPEED_MULTIPLIER;
            }

            var span = _world.Where(out Aspect a);
            foreach (var e in span)
            {
                _rpc.Move(new MoveCommand()
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

            if (_input.IsDown(KeyboardKey.A) || _input.IsDown(KeyboardKey.Left))
            {
                currentInput.Y -= 1;
            }

            if (_input.IsDown(KeyboardKey.D) || _input.IsDown(KeyboardKey.Right))
            {
                currentInput.Y += 1;
            }

            if (_input.IsDown(KeyboardKey.W) || _input.IsDown(KeyboardKey.Up))
            {
                currentInput.X += 1;
            }

            if (_input.IsDown(KeyboardKey.S) || _input.IsDown(KeyboardKey.Down))
            {
                currentInput.X -= 1;
            }

            if (_input.IsDown(KeyboardKey.Q))
            {
                currentInput.Z += 1;
            }

            if (_input.IsDown(KeyboardKey.E))
            {
                currentInput.Z -= 1;
            }

            if (_input.IsPressing(KeyboardKey.Space))
            {
                currentInput *= SPACE_SPEED_MULTIPLIER;
            }

            Camera.Main.Rotate(_input.MouseDelta * (float)Time.DeltaTime / 2);
            Camera.Main.Move(currentInput * (float)Time.DeltaTime * 2);
        }
    }
}
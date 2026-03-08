using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Extensions;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class InputSystem : IEcsRun
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<LocalPlayer> player = Inc;
        public EcsReadonlyPool<Position> position = Inc;
        public EcsReadonlyPool<NetworkId> networkId = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    [DI] private Rpc _rpc;
    [DI] private Input _input;
    [DI] private ICamera _camera;
    [DI] private ICamera2D _camera2D;
    [DI] private Time _time = null!;
    [DI] private IRenderer _renderer;
    [DI] private Application _application;
    private const float SPACE_SPEED_MULTIPLIER = 5f;
    
    public void Run()
    {
        _camera2D.Position = _camera2D.Position;
        if (_renderer.WindowShouldClose())
        {
            _application.Stop();
        }
        
        if (_input.IsMouseRightButtonDown)
        {
            _input.LockCursor();
            return;
        }
        
        if (_input.IsMouseRightButtonUp)
        {
            _input.UnlockCursor();
            return;
        }

        if (_input.IsMouseLeftButtonHold)
        {
            Vector3 currentInput = Vector3.Zero;

            if (_input.IsDown(KeyboardKeys.A) || _input.IsDown(KeyboardKeys.Left))
            {
                currentInput.Y -= 1;
            }

            if (_input.IsDown(KeyboardKeys.D) || _input.IsDown(KeyboardKeys.Right))
            {
                currentInput.Y += 1;
            }

            if (_input.IsDown(KeyboardKeys.W) || _input.IsDown(KeyboardKeys.Up))
            {
                currentInput.X += 1;
            }

            if (_input.IsDown(KeyboardKeys.S) || _input.IsDown(KeyboardKeys.Down))
            {
                currentInput.X -= 1;
            }
            
            if (_input.IsDown(KeyboardKeys.Q))
            {
                currentInput.Z += 1;
            }

            if (_input.IsDown(KeyboardKeys.E))
            {
                currentInput.Z -= 1;
            }
            
            if (_input.IsPressing(KeyboardKeys.Space))
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
        
        if (_input.IsMouseLocked)
        {
            Vector3 currentInput = Vector3.Zero;

            if (_input.IsDown(KeyboardKeys.A) || _input.IsDown(KeyboardKeys.Left))
            {
                currentInput.Y -= 1;
            }

            if (_input.IsDown(KeyboardKeys.D) || _input.IsDown(KeyboardKeys.Right))
            {
                currentInput.Y += 1;
            }

            if (_input.IsDown(KeyboardKeys.W) || _input.IsDown(KeyboardKeys.Up))
            {
                currentInput.X += 1;
            }

            if (_input.IsDown(KeyboardKeys.S) || _input.IsDown(KeyboardKeys.Down))
            {
                currentInput.X -= 1;
            }

            if (_input.IsDown(KeyboardKeys.Q))
            {
                currentInput.Z += 1;
            }

            if (_input.IsDown(KeyboardKeys.E))
            {
                currentInput.Z -= 1;
            }

            if (_input.IsPressing(KeyboardKeys.Space))
            {
                currentInput *= SPACE_SPEED_MULTIPLIER;
            }

            _camera.Rotate(_input.MouseDelta * (float)_time.DeltaTime / 2);
            _camera.Move(currentInput * (float)_time.DeltaTime * 2);
        }
    }
}
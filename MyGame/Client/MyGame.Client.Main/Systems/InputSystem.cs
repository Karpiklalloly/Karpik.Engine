using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Extensions;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Physics.Core;
using Veldrid;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

[SequentialSystem]
public class InputSystem : ISystemUpdate
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<LocalPlayer> player = Inc;
        public EcsReadonlyPool<Transform2D> position = Inc;
        public EcsReadonlyPool<NetworkId> networkId = Inc;
    }
    
    [DI] private DefaultWorld _world;
    [DI] private Rpc _rpc;
    [DI] private Input _input;
    // [DI] private ICamera _camera;
    // [DI] private ICamera2D _camera2D;
    [DI] private Time _time = null!;
    // [DI] private IRenderer _renderer;
    [DI] private Application _application;
    
    public void Update()
    {
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

        // Platformer input - send to server
        float moveX = 0;
        bool jump = _input.IsPressed(Key.Space) || _input.IsPressed(Key.W) || _input.IsPressed(Key.Up);
        
        if (_input.IsDown(Key.A) || _input.IsDown(Key.Left))
        {
            moveX -= 1;
        }

        if (_input.IsDown(Key.D) || _input.IsDown(Key.Right))
        {
            moveX += 1;
        }

        var span = _world.Where(out Aspect a);
        if (moveX != 0)
        {
            foreach (var e in span)
            {
                _rpc.PlatformerInput(new PlatformerInputCommand
                {
                    MoveX = moveX,
                    Target = a.networkId.Get(e).Id
                }, DeliveryMethod.Sequenced);
            }
        }

        if (jump)
        {
            foreach (var e in span)
            {
                _rpc.Jump(new JumpCommand
                {
                    Target = a.networkId.Get(e).Id
                }, DeliveryMethod.ReliableOrdered);
            }
        }
        
        if (_input.IsMouseLocked)
        {
            Vector3 currentInput = Vector3.Zero;

            if (_input.IsDown(Key.A) || _input.IsDown(Key.Left))
            {
                currentInput.Y -= 1;
            }

            if (_input.IsDown(Key.D) || _input.IsDown(Key.Right))
            {
                currentInput.Y += 1;
            }

            if (_input.IsDown(Key.W) || _input.IsDown(Key.Up))
            {
                currentInput.X += 1;
            }

            if (_input.IsDown(Key.S) || _input.IsDown(Key.Down))
            {
                currentInput.X -= 1;
            }

            if (_input.IsDown(Key.Q))
            {
                currentInput.Z += 1;
            }

            if (_input.IsDown(Key.E))
            {
                currentInput.Z -= 1;
            }

            if (_input.IsPressing(Key.Space))
            {
                currentInput *= 5f;
            }

            // _camera.Rotate(_input.MouseDelta * (float)_time.DeltaTime / 2);
            // _camera.Move(currentInput * (float)_time.DeltaTime * 2);
        }
    }
}

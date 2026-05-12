using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

public class KinematicControllerSystem : ISystemUpdate
{
    class Aspect : EcsAspect
    {
        public EcsPool<PhysicsBodyRef> body = Inc;
        public EcsPool<Transform2D> transform = Inc;
        public EcsPool<KinematicCharacterController> controller = Inc;
        public EcsPool<WannaMove> move = Inc;
    }
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D = null!;
    [DI] private Time _time = null!;
    
    private RaycastHit2D[] _hits = new RaycastHit2D[16];
    
    public void Update()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref var ctrl = ref a.controller.Get(e);
            ref var bodyRef = ref a.body.Get(e);
            ref var transform = ref a.transform.Get(e);
            
            float moveX = 0;
            bool jump = false;
            
            ref var wanna = ref a.move.Get(e);
            moveX = wanna.MoveX;
            jump = wanna.Jump;
            // Сбросить после использования
            wanna.MoveX = 0;
            wanna.Jump = false;
            
            Vector2 velocity = _physicsWorld2D.GetVelocity(bodyRef.Handle);

            float dt = (float)_time.FixedDeltaTime;
            
            // Целевая скорость по X
            float targetVelX = moveX * ctrl.MoveSpeed;
            // Плавное замедление (LERP)
            velocity.X = velocity.X + (targetVelX - velocity.X) * 10f * dt;
            
            if (ctrl.IsGrounded)
            {
                velocity.Y = 0;
                
                bool canJump = (float)_time.TotalTime - ctrl.LastJumpTime > ctrl.JumpCooldown;
                if (jump && canJump)
                {
                    velocity.Y = ctrl.JumpForce;
                    ctrl.LastJumpTime = (float)_time.TotalTime;
                    ctrl.IsGrounded = false;
                }
            }
            else
            {
                velocity.Y -= ctrl.Gravity * dt;
                velocity.Y = MathF.Max(velocity.Y, -ctrl.MaxFallSpeed);
            }
            
            Vector2 proposedPos = transform.Position + velocity * dt;
            
            // Проверить X движение
            Vector2 testPos = proposedPos;
            testPos.Y = transform.Position.Y; // Y не меняем
            Vector2 rayStart = testPos + new Vector2(0, 0.9f);
            Vector2 rayEnd = testPos - new Vector2(0, 0.9f);
            
            int hitCount = _physicsWorld2D.Raycast(rayStart, rayEnd, Physics2DLayers.Platform, _hits);
            if (hitCount > 0 && velocity.X != 0)
            {
                velocity.X = 0;
                proposedPos.X = transform.Position.X;
            }
            
            // Проверить Y движение
            rayStart = proposedPos + new Vector2(0, 1f);
            rayEnd = proposedPos - new Vector2(0, 1f);
            
            hitCount = _physicsWorld2D.Raycast(rayStart, rayEnd, Physics2DLayers.Platform, _hits);
            if (hitCount > 0)
            {
                if (velocity.Y < 0) // Падаем вниз
                {
                    ctrl.IsGrounded = true;
                    velocity.Y = 0;
                    proposedPos.Y = _hits[0].Point.Y + 1f; // На поверхность
                }
                else if (velocity.Y > 0) // Прыгаем вверх
                {
                    velocity.Y = 0;
                    proposedPos.Y = _hits[0].Point.Y - 1f;
                }
            }
            else if (velocity.Y < 0)
            {
                ctrl.IsGrounded = false;
            }
            
            // Применить скорость и позицию
            velocity.Y = 0;
            _physicsWorld2D.SetVelocity(bodyRef.Handle, velocity);
            
            Array.Clear(_hits, 0, _hits.Length);
        }
    }
}
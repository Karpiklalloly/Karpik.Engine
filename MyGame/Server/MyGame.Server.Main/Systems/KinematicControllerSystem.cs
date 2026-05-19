using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

public class KinematicControllerSystem : ISystemFixedUpdate
{
    private const float HALF_HEIGHT = 1.0f;
    private const float SKIN_WIDTH = 1.0f;
    
    class Aspect : EcsAspect
    {
        public EcsPool<PhysicsBodyRef> body = Inc;
        public EcsPool<Transform2D> transform = Inc;
        public EcsPool<KinematicCharacterController> controller = Inc;
        public EcsPool<WannaMove> move = Opt;
    }
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D = null!;
    [DI] private Time _time = null!;
    
    private RaycastHit2D[] _hits = new RaycastHit2D[16];
    
    public void FixedUpdate()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref var controller = ref a.controller.Get(e);
            ref var bodyRef = ref a.body.Get(e);
            ref var transform = ref a.transform.Get(e);

            Vector2 targetVelocity = _physicsWorld2D.GetVelocity(bodyRef.Handle);
            float moveX = 0f;
            bool jump = false;
            bool hasMove = a.move.Has(e);
            if (hasMove)
            {
                ref var move = ref a.move.Get(e);
                moveX = move.MoveX;
                jump = move.Jump;
            }

            CheckGround(ref controller, ref bodyRef, ref transform, targetVelocity);
            ApplyHorizontalInput(ref controller, moveX, ref targetVelocity);
            ApplyGravity(ref controller, ref targetVelocity);
            ApplyJump(ref controller, jump, ref targetVelocity);
            
            if (controller.IsCeiled && targetVelocity.Y > 0)
            {
                targetVelocity.Y = 0;
            }

            if (controller.TouchLeft && targetVelocity.X < 0)
            {
                targetVelocity.X = 0;
            }

            if (controller.TouchRight && targetVelocity.X > 0)
            {
                targetVelocity.X = 0;
            }

            _physicsWorld2D.SetVelocity(bodyRef.Handle, targetVelocity);

            if (hasMove)
            {
                ref var move = ref a.move.Get(e);
                move.MoveX = 0;
                move.Jump = false;
            }
        }
    }

    private void CheckGround(
        ref KinematicCharacterController controller,
        ref PhysicsBodyRef bodyRef,
        ref Transform2D transform,
        Vector2 velocity)
    {
        controller.IsGrounded = false;
        controller.IsCeiled = false;
        controller.TouchLeft = false;
        controller.TouchRight = false;
        controller.GroundNormal = Vector2.UnitY;
        Vector2 position = transform.Position;
        float fixedDeltaTime = (float)_time.FixedDeltaTime;
        float downCastDistance = KinematicCharacterController.GROUND_CHECK_DISTANCE + MathF.Max(0f, -velocity.Y * fixedDeltaTime);
        float upCastDistance = KinematicCharacterController.GROUND_CHECK_DISTANCE + MathF.Max(0f, velocity.Y * fixedDeltaTime);
        float sideCastDistance = KinematicCharacterController.GROUND_CHECK_DISTANCE
            + MathF.Max(MathF.Abs(velocity.X), controller.MoveSpeed) * fixedDeltaTime;

        Vector2 leftHead = position + new Vector2(-SKIN_WIDTH / 2, HALF_HEIGHT);
        Vector2 rightHead = position + new Vector2(SKIN_WIDTH / 2, HALF_HEIGHT);
        CastCeilRay(leftHead, ref bodyRef, ref controller, _hits, upCastDistance);
        CastCeilRay(rightHead, ref bodyRef, ref controller, _hits, upCastDistance);
        
        Vector2 leftFoot = position + new Vector2(-SKIN_WIDTH / 2, -HALF_HEIGHT);
        Vector2 rightFoot = position + new Vector2(SKIN_WIDTH / 2, -HALF_HEIGHT);
        CastGroundRay(leftFoot, ref bodyRef, ref controller, _hits, downCastDistance);
        CastGroundRay(rightFoot, ref bodyRef, ref controller, _hits, downCastDistance);
        
        CastWallRay(position + new Vector2(0, HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0), sideCastDistance);
        CastWallRay(position + new Vector2(0, 0), ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0), sideCastDistance);
        CastWallRay(position + new Vector2(0, -HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0), sideCastDistance);
        
        CastWallRay(position + new Vector2(0, HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0), sideCastDistance);
        CastWallRay(position + new Vector2(0, 0), ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0), sideCastDistance);
        CastWallRay(position + new Vector2(0, -HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0), sideCastDistance);
    }

    private void CastGroundRay(Vector2 from, ref PhysicsBodyRef bodyRef, ref KinematicCharacterController controller, Span<RaycastHit2D> hits, float distance)
    {
        Vector2 to = from + new Vector2(0, -distance);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            float angle = MathF.Acos(Math.Clamp(Vector2.Dot(hit.Normal, Vector2.UnitY), -1, 1));
            if (angle <= controller.MaxGroundAngle)
            {
                controller.IsGrounded = true;
                controller.GroundNormal = hit.Normal;
                return;
            }
        }
    }

    private void CastCeilRay(Vector2 from, ref PhysicsBodyRef bodyRef, ref KinematicCharacterController controller, Span<RaycastHit2D> hits, float distance)
    {
        Vector2 to = from + new Vector2(0, distance);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            float angle = MathF.Acos(Math.Clamp(Vector2.Dot(hit.Normal, -Vector2.UnitY), -1, 1));
            if (angle <= controller.MaxGroundAngle)
            {
                controller.IsCeiled = true;
                return;
            }
        }
    }
    
    private void CastWallRay(Vector2 from, ref PhysicsBodyRef bodyRef, ref KinematicCharacterController controller, Span<RaycastHit2D> hits, Vector2 direction, float distance)
    {
        Vector2 to = from + direction + new Vector2(MathF.Sign(direction.X) * distance, 0);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            if (direction.X > 0f && hit.Normal.X < -0.7f)
            {
                controller.TouchRight = true;
            }
            
            if (direction.X < 0f && hit.Normal.X > 0.7f)
            {
                controller.TouchLeft = true;
            }
        }
    }
    
    private void ApplyHorizontalInput(ref KinematicCharacterController controller, float moveX, ref Vector2 targetVelocity)
    {
        if (controller.IsGrounded)
        {
            Vector2 tangent = new Vector2(controller.GroundNormal.Y, -controller.GroundNormal.X);
            targetVelocity = tangent * (moveX * controller.MoveSpeed);
        }
        else if (controller.IsCeiled)
        {
            Vector2 tangent = new Vector2(controller.GroundNormal.Y, -controller.GroundNormal.X);
            targetVelocity.X = tangent.X * moveX * controller.MoveSpeed;
        }
        else
        {
            targetVelocity.X = moveX * controller.MoveSpeed;
        }
    }

    private void ApplyGravity(ref KinematicCharacterController controller, ref Vector2 targetVelocity)
    {
        if (controller.IsGrounded)
        {
            return;
        }

        targetVelocity.Y += controller.Gravity * (float)_time.FixedDeltaTime;
        targetVelocity.Y = MathF.Max(targetVelocity.Y, -controller.MaxFallSpeed);
    }

    private void ApplyJump(ref KinematicCharacterController controller, bool jump, ref Vector2 targetVelocity)
    {
        float now = (float)_time.TotalTime;
        if (jump && controller.IsGrounded && now - controller.LastJumpTime >= controller.JumpCooldown)
        {
            targetVelocity.Y = controller.JumpSpeed;
            controller.IsGrounded = false;
            controller.LastJumpTime = now;
        }
    }
}

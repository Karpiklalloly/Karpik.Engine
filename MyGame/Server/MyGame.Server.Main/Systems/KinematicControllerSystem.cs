using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

public class KinematicControllerSystem : ISystemFixedUpdate
{
    private const float HALF_HEIGHT = 1.0f;
    private const float SKIN_WIDTH = 1.0f;
    private const float WALL_SKIN_WIDTH = 0.03f;
    private const float VERTICAL_SKIN_WIDTH = 0.03f;
    private const float GROUND_CONTACT_TOLERANCE = 0.08f;
    private const float RAY_SPACING = 0.35f;
    
    class Aspect : EcsAspect
    {
        public EcsPool<PhysicsBodyRef> body = Inc;
        public EcsPool<Transform2D> transform = Inc;
        public EcsPool<KinematicCharacterController> controller = Inc;
        public EcsPool<WannaMove> move = Opt;
        public EcsReadonlyPool<PhysicsBox> boxes = Opt;
    }
    
    [DI] private DefaultWorld _world = null!;
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

            CheckGround(ref controller, ref bodyRef, ref transform, ref targetVelocity);
            ApplyHorizontalInput(ref controller, moveX, ref targetVelocity);
            ApplyGravity(ref controller, ref targetVelocity);
            bool jumped = ApplyJump(ref controller, jump, ref targetVelocity);
            if (!controller.IsGrounded && !jumped)
            {
                CheckGround(ref controller, ref bodyRef, ref transform, ref targetVelocity);
            }

            CheckWalls(ref controller, ref bodyRef, ref transform, ref targetVelocity, a.boxes);
            
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
        ref Vector2 targetVelocity)
    {
        controller.IsGrounded = false;
        controller.IsCeiled = false;
        controller.TouchLeft = false;
        controller.TouchRight = false;
        controller.GroundNormal = Vector2.UnitY;
        Vector2 position = transform.Position;
        float fixedDeltaTime = (float)_time.FixedDeltaTime;
        float downCastDistance = KinematicCharacterController.GROUND_CHECK_DISTANCE + MathF.Max(0f, -targetVelocity.Y * fixedDeltaTime);
        float upCastDistance = KinematicCharacterController.GROUND_CHECK_DISTANCE + MathF.Max(0f, targetVelocity.Y * fixedDeltaTime);

        int verticalRayCount = GetRayCount(SKIN_WIDTH);
        for (int i = 0; i < verticalRayCount; i++)
        {
            float x = GetRayOffset(i, verticalRayCount, SKIN_WIDTH / 2f);
            CastCeilRay(position + new Vector2(x, HALF_HEIGHT), ref bodyRef, ref controller, _hits, upCastDistance, ref targetVelocity, fixedDeltaTime);
            CastGroundRay(position + new Vector2(x, -HALF_HEIGHT), ref bodyRef, ref controller, _hits, downCastDistance, ref targetVelocity, fixedDeltaTime);
        }
    }

    private void CheckWalls(
        ref KinematicCharacterController controller,
        ref PhysicsBodyRef bodyRef,
        ref Transform2D transform,
        ref Vector2 targetVelocity,
        EcsReadonlyPool<PhysicsBox> boxes)
    {
        float fixedDeltaTime = (float)_time.FixedDeltaTime;
        float sideCastDistance = KinematicCharacterController.GROUND_CHECK_DISTANCE + MathF.Abs(targetVelocity.X * fixedDeltaTime);
        Vector2 position = transform.Position;

        int sideRayCount = GetRayCount(HALF_HEIGHT * 2f);
        for (int i = 0; i < sideRayCount; i++)
        {
            float y = GetRayOffset(i, sideRayCount, HALF_HEIGHT);
            Vector2 sideOrigin = position + new Vector2(0, y);
            CastWallRay(sideOrigin, ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0), sideCastDistance, SKIN_WIDTH / 2f, ref targetVelocity, fixedDeltaTime, boxes);
            CastWallRay(sideOrigin, ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0), sideCastDistance, SKIN_WIDTH / 2f, ref targetVelocity, fixedDeltaTime, boxes);
        }
    }

    private void CastGroundRay(
        Vector2 from,
        ref PhysicsBodyRef bodyRef,
        ref KinematicCharacterController controller,
        Span<RaycastHit2D> hits,
        float distance,
        ref Vector2 targetVelocity,
        float fixedDeltaTime)
    {
        Vector2 to = from + new Vector2(0, -distance);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        bool hasGroundHit = false;
        float closestGap = float.MaxValue;
        Vector2 closestNormal = Vector2.UnitY;
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
                float gapFromFoot = hit.Fraction * distance;
                if (gapFromFoot < closestGap)
                {
                    hasGroundHit = true;
                    closestGap = gapFromFoot;
                    closestNormal = hit.Normal;
                }
            }
        }

        if (!hasGroundHit)
        {
            return;
        }

        if (closestGap <= VERTICAL_SKIN_WIDTH || IsRestingOnGround(closestGap, targetVelocity.Y))
        {
            controller.IsGrounded = true;
            controller.GroundNormal = closestNormal;
            if (targetVelocity.Y < 0f)
            {
                targetVelocity.Y = 0f;
            }

            return;
        }

        if (targetVelocity.Y < 0f)
        {
            float maxFallVelocity = (closestGap - VERTICAL_SKIN_WIDTH) / fixedDeltaTime;
            targetVelocity.Y = MathF.Max(targetVelocity.Y, -maxFallVelocity);
        }
    }

    private void CastCeilRay(
        Vector2 from,
        ref PhysicsBodyRef bodyRef,
        ref KinematicCharacterController controller,
        Span<RaycastHit2D> hits,
        float distance,
        ref Vector2 targetVelocity,
        float fixedDeltaTime)
    {
        Vector2 to = from + new Vector2(0, distance);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        bool hasCeilHit = false;
        float closestGap = float.MaxValue;
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
                float gapFromHead = hit.Fraction * distance;
                if (gapFromHead < closestGap)
                {
                    hasCeilHit = true;
                    closestGap = gapFromHead;
                }
            }
        }

        if (!hasCeilHit)
        {
            return;
        }

        if (closestGap <= VERTICAL_SKIN_WIDTH)
        {
            controller.IsCeiled = true;
            if (targetVelocity.Y > 0f)
            {
                targetVelocity.Y = 0f;
            }

            return;
        }

        if (targetVelocity.Y > 0f)
        {
            float maxRiseVelocity = (closestGap - VERTICAL_SKIN_WIDTH) / fixedDeltaTime;
            targetVelocity.Y = MathF.Min(targetVelocity.Y, maxRiseVelocity);
        }
    }
    
    private void CastWallRay(
        Vector2 from,
        ref PhysicsBodyRef bodyRef,
        ref KinematicCharacterController controller,
        Span<RaycastHit2D> hits,
        Vector2 direction,
        float distance,
        float extent,
        ref Vector2 targetVelocity,
        float fixedDeltaTime,
        EcsReadonlyPool<PhysicsBox> boxes)
    {
        Vector2 to = from + direction + new Vector2(MathF.Sign(direction.X) * distance, 0);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        float rayLength = extent + distance;
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            if (IsPushableBox(hit.Entity, boxes))
            {
                continue;
            }

            float distanceFromOrigin = hit.Fraction * rayLength;
            float gapFromEdge = distanceFromOrigin - extent;
            if (direction.X > 0f && hit.Normal.X < -0.7f)
            {
                ClampRightWall(ref controller, ref targetVelocity, gapFromEdge, fixedDeltaTime);
            }
            
            if (direction.X < 0f && hit.Normal.X > 0.7f)
            {
                ClampLeftWall(ref controller, ref targetVelocity, gapFromEdge, fixedDeltaTime);
            }
        }
    }

    private static bool IsPushableBox(int entity, EcsReadonlyPool<PhysicsBox> boxes)
    {
        return entity >= 0 && boxes.Has(entity);
    }

    private static bool IsRestingOnGround(float gapFromFoot, float verticalVelocity)
    {
        return verticalVelocity <= 0f && gapFromFoot <= GROUND_CONTACT_TOLERANCE;
    }

    private static void ClampRightWall(ref KinematicCharacterController controller, ref Vector2 targetVelocity, float gapFromEdge, float fixedDeltaTime)
    {
        if (gapFromEdge <= WALL_SKIN_WIDTH)
        {
            controller.TouchRight = true;
            if (targetVelocity.X > 0f)
            {
                targetVelocity.X = 0f;
            }

            return;
        }

        if (targetVelocity.X <= 0f)
        {
            return;
        }

        float maxVelocity = (gapFromEdge - WALL_SKIN_WIDTH) / fixedDeltaTime;
        targetVelocity.X = MathF.Min(targetVelocity.X, maxVelocity);
    }

    private static void ClampLeftWall(ref KinematicCharacterController controller, ref Vector2 targetVelocity, float gapFromEdge, float fixedDeltaTime)
    {
        if (gapFromEdge <= WALL_SKIN_WIDTH)
        {
            controller.TouchLeft = true;
            if (targetVelocity.X < 0f)
            {
                targetVelocity.X = 0f;
            }

            return;
        }

        if (targetVelocity.X >= 0f)
        {
            return;
        }

        float maxVelocity = (gapFromEdge - WALL_SKIN_WIDTH) / fixedDeltaTime;
        targetVelocity.X = MathF.Max(targetVelocity.X, -maxVelocity);
    }

    private static int GetRayCount(float size)
    {
        return Math.Max(2, (int)MathF.Ceiling(size / RAY_SPACING) + 1);
    }

    private static float GetRayOffset(int index, int count, float halfSize)
    {
        if (count <= 1)
        {
            return 0f;
        }

        return -halfSize + (halfSize * 2f) * index / (count - 1);
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

    private bool ApplyJump(ref KinematicCharacterController controller, bool jump, ref Vector2 targetVelocity)
    {
        float now = (float)_time.TotalTime;
        if (jump && controller.IsGrounded && now - controller.LastJumpTime >= controller.JumpCooldown)
        {
            targetVelocity.Y = controller.JumpSpeed;
            controller.IsGrounded = false;
            controller.LastJumpTime = now;
            return true;
        }

        return false;
    }
}

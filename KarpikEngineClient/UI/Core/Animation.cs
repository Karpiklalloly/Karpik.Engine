using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Animation
{
    public float Duration { get; set; }
    public float ElapsedTime { get; private set; }
    public bool IsCompleted => ElapsedTime >= Duration;
    public bool IsPlaying { get; private set; }
    
    public EasingFunction Easing { get; set; } = EasingFunction.Linear;
    
    private readonly Action<float> _updateCallback;
    private readonly Action? _completedCallback;
    
    public Animation(float duration, Action<float> updateCallback, Action? completedCallback = null)
    {
        Duration = duration;
        _updateCallback = updateCallback;
        _completedCallback = completedCallback;
    }
    
    public void Start()
    {
        IsPlaying = true;
        ElapsedTime = 0f;
    }
    
    public void Stop()
    {
        IsPlaying = false;
    }
    
    public void Reset()
    {
        ElapsedTime = 0f;
        IsPlaying = false;
    }
    
    public void Update(float deltaTime)
    {
        if (!IsPlaying || IsCompleted) return;
        
        ElapsedTime += deltaTime;
        
        var progress = Math.Clamp(ElapsedTime / Duration, 0f, 1f);
        var easedProgress = ApplyEasing(progress);
        
        _updateCallback(easedProgress);
        
        if (IsCompleted)
        {
            IsPlaying = false;
            _completedCallback?.Invoke();
        }
    }
    
    private float ApplyEasing(float t)
    {
        return Easing switch
        {
            EasingFunction.Linear => t,
            EasingFunction.EaseInQuad => t * t,
            EasingFunction.EaseOutQuad => 1f - (1f - t) * (1f - t),
            EasingFunction.EaseInOutQuad => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
            EasingFunction.EaseInCubic => t * t * t,
            EasingFunction.EaseOutCubic => 1f - MathF.Pow(1f - t, 3f),
            EasingFunction.EaseInOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
            EasingFunction.EaseInSine => 1f - MathF.Cos(t * MathF.PI / 2f),
            EasingFunction.EaseOutSine => MathF.Sin(t * MathF.PI / 2f),
            EasingFunction.EaseInOutSine => -(MathF.Cos(MathF.PI * t) - 1f) / 2f,
            _ => t
        };
    }
    
    // Статические методы для создания распространенных анимаций
    public static Animation FadeIn(VisualElement element, float duration = 0.3f, Action? onComplete = null)
    {
        var startAlpha = element.Style.BackgroundColor.A;
        var targetAlpha = 255;
        
        return new Animation(duration, progress =>
        {
            var alpha = (byte)(startAlpha + (targetAlpha - startAlpha) * progress);
            var color = element.Style.BackgroundColor;
            element.Style.BackgroundColor = new Color(color.R, color.G, color.B, alpha);
        }, onComplete);
    }
    
    public static Animation FadeOut(VisualElement element, float duration = 0.3f, Action? onComplete = null)
    {
        var startAlpha = element.Style.BackgroundColor.A;
        
        return new Animation(duration, progress =>
        {
            var alpha = (byte)(startAlpha * (1f - progress));
            var color = element.Style.BackgroundColor;
            element.Style.BackgroundColor = new Color(color.R, color.G, color.B, alpha);
        }, onComplete);
    }
    
    public static Animation SlideIn(VisualElement element, Vector2 fromOffset, float duration = 0.3f, Action? onComplete = null)
    {
        var startPos = element.Position;
        var targetPos = startPos;
        element.Position = startPos + fromOffset;
        
        return new Animation(duration, progress =>
        {
            element.Position = Vector2.Lerp(startPos + fromOffset, targetPos, progress);
        }, onComplete)
        {
            Easing = EasingFunction.EaseOutCubic
        };
    }
    
    public static Animation Scale(VisualElement element, Vector2 fromScale, Vector2 toScale, float duration = 0.3f, Action? onComplete = null)
    {
        var originalSize = element.Size;
        
        return new Animation(duration, progress =>
        {
            var currentScale = Vector2.Lerp(fromScale, toScale, progress);
            element.Size = originalSize * currentScale;
        }, onComplete)
        {
            Easing = EasingFunction.EaseOutCubic
        };
    }
}

public enum EasingFunction
{
    Linear,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutQuad,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic,
    EaseInSine,
    EaseOutSine,
    EaseInOutSine
}

public class AnimationManager
{
    private readonly List<Animation> _animations = new();
    
    public void AddAnimation(Animation animation)
    {
        _animations.Add(animation);
        animation.Start();
    }
    
    public void Update(float deltaTime)
    {
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            var animation = _animations[i];
            animation.Update(deltaTime);
            
            if (animation.IsCompleted)
            {
                _animations.RemoveAt(i);
            }
        }
    }
    
    public void Clear()
    {
        _animations.Clear();
    }
}
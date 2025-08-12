using System.Numerics;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client.UI.Extensions;

public static class VisualElementTweenExtensions
{
    /// <summary>
    /// Анимирует позицию элемента
    /// </summary>
    public static GTween TweenPosition(this VisualElement element, Vector2 to, float duration, bool pausable = false)
    {
        // Отключаем layout во время анимации позиции
        element.IgnoreLayout = true;
        
        // Создаем твин для каждой координаты отдельно
        var tweenX = GTweenExtensions.Tween(
            () => element.Position.X,
            valueX => {
                element.Position = new Vector2(valueX, element.Position.Y);
            },
            to.X,
            duration
        );
        
        var tweenY = GTweenExtensions.Tween(
            () => element.Position.Y,
            valueY => {
                element.Position = new Vector2(element.Position.X, valueY);
            },
            to.Y,
            duration
        );
        
        var tween = tweenX.OnComplete(() => {
            element.IgnoreLayout = false;
        }).OnKill(() => {
            element.IgnoreLayout = false;
        });
        
        Tween.Add(tweenX, pausable);
        Tween.Add(tweenY, pausable);
        return tween;
    }

    /// <summary>
    /// Анимирует размер элемента
    /// </summary>
    public static GTween TweenSize(this VisualElement element, Vector2 to, float duration, bool pausable = false)
    {
        // Отключаем layout во время анимации размера
        element.IgnoreLayout = true;
        
        var tween = GTweenExtensions.Tween(
            () => element.Size,
            value => element.Size = value,
            to,
            duration
        ).OnComplete(() => element.IgnoreLayout = false)
         .OnKill(() => element.IgnoreLayout = false);
        
        Tween.Add(tween, pausable);
        return tween;
    }

    /// <summary>
    /// Анимирует прозрачность фона элемента
    /// </summary>
    public static GTween TweenBackgroundAlpha(this VisualElement element, byte to, float duration, bool pausable = false)
    {
        var currentColor = element.Style.GetBackgroundColorOrDefault();
        
        var tween = GTweenExtensions.Tween(
            () => (int)currentColor.A,
            value => 
            {
                var newColor = new Color(currentColor.R, currentColor.G, currentColor.B, (byte)value);
                element.Style.BackgroundColor = newColor;
                currentColor = newColor;
            },
            (int)to,
            duration
        );
        
        Tween.Add(tween, pausable);
        return tween;
    }

    /// <summary>
    /// Анимирует цвет фона элемента
    /// </summary>
    public static GTween TweenBackgroundColor(this VisualElement element, Color to, float duration, bool pausable = false)
    {
        var startColor = element.Style.GetBackgroundColorOrDefault();
        
        var tween = GTweenExtensions.Tween(
            () => 0f,
            progress =>
            {
                var r = (byte)(startColor.R + (to.R - startColor.R) * progress);
                var g = (byte)(startColor.G + (to.G - startColor.G) * progress);
                var b = (byte)(startColor.B + (to.B - startColor.B) * progress);
                var a = (byte)(startColor.A + (to.A - startColor.A) * progress);
                element.Style.BackgroundColor = new Color(r, g, b, a);
            },
            1f,
            duration
        );
        
        Tween.Add(tween, pausable);
        return tween;
    }

    /// <summary>
    /// Плавное появление элемента (fade in)
    /// </summary>
    public static GTween FadeIn(this VisualElement element, float duration = 0.3f, bool pausable = false)
    {
        var currentColor = element.Style.GetBackgroundColorOrDefault();
        element.Style.BackgroundColor = new Color(currentColor.R, currentColor.G, currentColor.B, (byte)0);
        
        return element.TweenBackgroundAlpha(255, duration, pausable)
            .SetEasing(Easing.OutCubic);
    }

    /// <summary>
    /// Плавное исчезновение элемента (fade out)
    /// </summary>
    public static GTween FadeOut(this VisualElement element, float duration = 0.3f, bool pausable = false)
    {
        return element.TweenBackgroundAlpha(0, duration, pausable)
            .SetEasing(Easing.OutCubic);
    }

    /// <summary>
    /// Анимация появления с увеличением размера
    /// </summary>
    public static GTween ScaleIn(this VisualElement element, float duration = 0.3f, bool pausable = false)
    {
        var originalSize = element.Size;
        element.Size = Vector2.Zero;
        
        return element.TweenSize(originalSize, duration, pausable)
            .SetEasing(Easing.OutBack);
    }

    /// <summary>
    /// Анимация исчезновения с уменьшением размера
    /// </summary>
    public static GTween ScaleOut(this VisualElement element, float duration = 0.3f, bool pausable = false)
    {
        return element.TweenSize(Vector2.Zero, duration, pausable)
            .SetEasing(Easing.InBack);
    }

    /// <summary>
    /// Анимация появления со сдвигом
    /// </summary>
    public static GTween SlideIn(this VisualElement element, Vector2 fromOffset, float duration = 0.3f, bool pausable = false)
    {
        var targetPosition = element.Position;
        var startPosition = targetPosition + fromOffset;
        
        // Отключаем layout сразу, чтобы предотвратить перезапись позиции
        element.IgnoreLayout = true;
        
        // Устанавливаем начальную позицию
        element.Position = startPosition;
        
        // Создаем твин напрямую, чтобы избежать двойного вызова IgnoreLayout
        var tween = GTweenExtensions.Tween(
            () => element.Position,
            value => element.Position = value,
            targetPosition,
            duration
        ).SetEasing(Easing.OutCubic)
         .OnComplete(() => element.IgnoreLayout = false)
         .OnKill(() => element.IgnoreLayout = false);
        
        Tween.Add(tween, pausable);
        return tween;
    }

    /// <summary>
    /// Анимация исчезновения со сдвигом
    /// </summary>
    public static GTween SlideOut(this VisualElement element, Vector2 toOffset, float duration = 0.3f, bool pausable = false)
    {
        var targetPosition = element.Position + toOffset;
        
        return element.TweenPosition(targetPosition, duration, pausable)
            .SetEasing(Easing.InCubic);
    }

    /// <summary>
    /// Анимация покачивания (shake)
    /// </summary>
    public static GTween Shake(this VisualElement element, float intensity = 10f, float duration = 0.5f, bool pausable = false)
    {
        var originalPosition = element.Position;
        var random = new Random();
        
        // Отключаем layout во время анимации
        element.IgnoreLayout = true;
        
        var tween = GTweenExtensions.Tween(
            () => 0f,
            progress =>
            {
                // Используем progress для создания затухающего эффекта
                var shake = intensity * (1f - progress);
                var frequency = 20f; // Частота колебаний
                var offsetX = (float)Math.Sin(progress * frequency * Math.PI) * shake * (float)(random.NextDouble() * 2 - 1);
                var offsetY = (float)Math.Cos(progress * frequency * Math.PI * 1.1) * shake * (float)(random.NextDouble() * 2 - 1);
                var newPos = originalPosition + new Vector2(offsetX, offsetY);
                element.Position = newPos;
            },
            1f,
            duration
        ).OnComplete(() => {
            element.Position = originalPosition;
            element.IgnoreLayout = false;
        }).OnKill(() => {
            element.Position = originalPosition;
            element.IgnoreLayout = false;
        });
        
        Tween.Add(tween, pausable);
        return tween;
    }

    /// <summary>
    /// Анимация пульсации размера
    /// </summary>
    public static GTween Pulse(this VisualElement element, float scale = 1.1f, float duration = 0.5f, bool pausable = false)
    {
        var originalSize = element.Size;
        var targetSize = originalSize * scale;
        
        var tween = element.TweenSize(targetSize, duration / 2, pausable)
            .SetEasing(Easing.OutQuad)
            .OnComplete(() =>
            {
                element.TweenSize(originalSize, duration / 2, pausable)
                    .SetEasing(Easing.InQuad);
            });
        
        return tween;
    }
}
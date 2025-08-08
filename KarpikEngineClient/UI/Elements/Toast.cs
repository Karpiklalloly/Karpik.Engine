using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Toast : VisualElement
{
    public string Message { get; set; }
    public ToastType Type { get; set; } = ToastType.Info;
    public float Duration { get; set; } = 3f;
    
    private float _timeRemaining;
    private bool _isShowing = false;
    
    public event Action? OnDismissed;
    
    public Toast(string message, ToastType type = ToastType.Info, float duration = 3f) : base("Toast")
    {
        Message = message;
        Type = type;
        Duration = duration;
        _timeRemaining = duration;
        
        AddClass("toast");
        SetupStyle();
        
        // Начинаем невидимыми
        Style.BackgroundColor = new Color(Style.BackgroundColor.R, Style.BackgroundColor.G, Style.BackgroundColor.B, (byte)0);
    }
    
    private void SetupStyle()
    {
        var baseColor = Type switch
        {
            ToastType.Success => new Color(76, 175, 80, 255),
            ToastType.Warning => new Color(255, 152, 0, 255),
            ToastType.Error => new Color(244, 67, 54, 255),
            _ => new Color(33, 150, 243, 255)
        };
        
        Style.BackgroundColor = baseColor;
        Style.TextColor = Color.White;
        Style.BorderRadius = 8;
        Style.Padding = new Padding(15, 10);
        Style.Margin = new Margin(10);
        Style.FontSize = 14;
    }
    
    public void Show()
    {
        if (_isShowing) return;
        
        _isShowing = true;
        _timeRemaining = Duration;
        
        // Анимация появления
        SlideIn(new Vector2(0, -50), 0.3f, () =>
        {
            // Начинаем отсчет времени после появления
            var fadeAnimation = new Animation(Duration, progress =>
            {
                // Ничего не делаем во время ожидания
            }, () =>
            {
                // Анимация исчезновения
                FadeOut(0.3f, () =>
                {
                    OnDismissed?.Invoke();
                });
            });
            
            AddAnimation(fadeAnimation);
        });
        
        // Анимация появления прозрачности
        FadeIn(0.3f);
    }
    
    public void Dismiss()
    {
        if (!_isShowing) return;
        
        FadeOut(0.3f, () =>
        {
            OnDismissed?.Invoke();
        });
    }
    
    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        
        if (_isShowing && _timeRemaining > 0)
        {
            _timeRemaining -= deltaTime;
        }
    }
    
    protected override void RenderSelf()
    {
        base.RenderSelf();
        
        if (!string.IsNullOrEmpty(Message))
        {
            var textPos = new Vector2(
                Position.X + ResolvedStyle.Padding.Left,
                Position.Y + (Size.Y - ResolvedStyle.FontSize) / 2
            );
            
            Raylib.DrawText(Message, (int)textPos.X, (int)textPos.Y, ResolvedStyle.FontSize, ResolvedStyle.TextColor);
        }
    }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public class ToastManager
{
    private readonly List<Toast> _toasts = new();
    private readonly VisualElement _container;
    
    public ToastManager(VisualElement container)
    {
        _container = container;
    }
    
    public void ShowToast(string message, ToastType type = ToastType.Info, float duration = 3f)
    {
        var toast = new Toast(message, type, duration);
        toast.OnDismissed += () =>
        {
            _container.RemoveChild(toast);
            _toasts.Remove(toast);
            RepositionToasts();
        };
        
        _toasts.Add(toast);
        _container.AddChild(toast);
        
        PositionToast(toast);
        toast.Show();
    }
    
    private void PositionToast(Toast toast)
    {
        var index = _toasts.IndexOf(toast);
        var yOffset = index * 60f; // 60px между уведомлениями
        
        toast.Position = new Vector2(_container.Size.X - 300 - 20, 20 + yOffset); // Правый верхний угол
        toast.Size = new Vector2(300, 50);
    }
    
    private void RepositionToasts()
    {
        for (int i = 0; i < _toasts.Count; i++)
        {
            var toast = _toasts[i];
            var targetY = 20 + i * 60f;
            
            // Анимируем перемещение
            var moveAnimation = new Animation(0.3f, progress =>
            {
                toast.Position = new Vector2(toast.Position.X, 
                    toast.Position.Y + (targetY - toast.Position.Y) * progress);
            })
            {
                Easing = EasingFunction.EaseOutCubic
            };
            
            toast.AddAnimation(moveAnimation);
        }
    }
}
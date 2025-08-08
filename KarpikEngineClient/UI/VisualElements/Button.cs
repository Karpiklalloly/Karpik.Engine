using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Button : VisualElement
{
    public string Text { get; set; }
    private bool _isPressed = false;
    
    public Button(string text = "Button") : base("Button")
    {
        Text = text;
        // Размеры по умолчанию
        Size = new Vector2(120, 35);
    }
    
    public override void Update(double deltaTime)
    {
        // Применяем стили и layout
        base.Update(deltaTime);
    }
    
    public override void Render()
    {
        if (!Visible) return;
        
        var computedStyle = GetComputedStyle();
        
        // Определяем цвет фона в зависимости от состояния
        Color backgroundColor = GetBackgroundColor(computedStyle);
        
        // Рисуем фон кнопки
        if (backgroundColor.A > 0)
        {
            Raylib.DrawRectangle(
                (int)Position.X, (int)Position.Y,
                (int)Size.X, (int)Size.Y,
                backgroundColor);
        }
        
        // Рисуем рамку
        if (computedStyle.BorderWidth.IsSet && computedStyle.BorderWidth.Value > 0 &&
            computedStyle.BorderColor.IsSet && computedStyle.BorderColor.Value.A > 0)
        {
            Raylib.DrawRectangleLinesEx(
                new Rectangle(Position.X, Position.Y, Size.X, Size.Y),
                computedStyle.BorderWidth.Value,
                computedStyle.BorderColor.Value);
        }
        
        // Рисуем текст
        DrawButtonText(computedStyle);
        
        // Рендерим детей
        base.Render();
    }
    
    private Color GetBackgroundColor(Style computedStyle)
    {
        // Приоритет: состояние > стиль > значение по умолчанию
        if (IsActive && computedStyle.BorderColor.IsSet)
        {
            // Более темный цвет при нажатии
            var baseColor = computedStyle.BackgroundColor.IsSet ? 
                           computedStyle.BackgroundColor.Value : Color.LightGray;
            return new Color(
                (byte)Math.Max(0, baseColor.R - 30),
                (byte)Math.Max(0, baseColor.G - 30),
                (byte)Math.Max(0, baseColor.B - 30),
                baseColor.A);
        }
        else if (computedStyle.BackgroundColor.IsSet)
        {
            return computedStyle.BackgroundColor.Value;
        }
        else
        {
            return IsHovered ? Color.Gray : Color.LightGray;
        }
    }
    
    private void DrawButtonText(Style computedStyle)
    {
        if (string.IsNullOrEmpty(Text)) return;
        
        // Получаем параметры текста из стилей
        int fontSize = computedStyle.FontSize.IsSet ? computedStyle.FontSize.Value : 16;
        Color textColor = computedStyle.Color.IsSet ? computedStyle.Color.Value : Color.Black;
        
        // Измеряем текст
        var textSize = Raylib.MeasureTextEx(Raylib.GetFontDefault(), Text, fontSize, 1);
        
        // Вычисляем позицию текста с учетом выравнивания
        float textX = Position.X;
        float textY = Position.Y;
        
        if (computedStyle.TextAlign.IsSet)
        {
            switch (computedStyle.TextAlign.Value)
            {
                case TextAlign.Center:
                    textX = Position.X + (Size.X - textSize.X) / 2;
                    break;
                case TextAlign.Right:
                    textX = Position.X + Size.X - textSize.X - 5; // 5px отступ
                    break;
                case TextAlign.Left:
                default:
                    textX = Position.X + 5; // 5px отступ
                    break;
            }
        }
        else
        {
            // По умолчанию - центрируем
            textX = Position.X + (Size.X - textSize.X) / 2;
        }
        
        // Центрируем по вертикали
        textY = Position.Y + (Size.Y - textSize.Y) / 2;
        
        // Рисуем текст
        Raylib.DrawText(Text, (int)textX, (int)textY, fontSize, textColor);
    }
    
    // Переопределяем обработчики событий для визуальной обратной связи
    public override void HandleMouseDown(MouseEvent mouseEvent)
    {
        _isPressed = true;
        base.HandleMouseDown(mouseEvent);
    }
    
    public override void HandleMouseUp(MouseEvent mouseEvent)
    {
        _isPressed = false;
        base.HandleMouseUp(mouseEvent);
    }
    
    public override void HandleMouseEnter()
    {
        base.HandleMouseEnter();
        // Можно добавить визуальные эффекты при наведении
    }
    
    public override void HandleMouseLeave()
    {
        _isPressed = false;
        base.HandleMouseLeave();
    }
}
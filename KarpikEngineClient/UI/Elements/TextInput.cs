using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class TextInput : VisualElement
{
    public string Text { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public bool IsReadOnly { get; set; } = false;
    public int MaxLength { get; set; } = 100;
    
    private int _cursorPosition = 0;
    private float _cursorBlinkTimer = 0f;
    private bool _showCursor = true;
    
    public event Action<string>? OnTextChanged;
    public event Action? OnEnterPressed;
    
    public TextInput(string placeholder = "") : base("TextInput")
    {
        Placeholder = placeholder;
        AddClass("textinput");
        
        AddManipulator(new FocusManipulator());
        Input.CharPressed += c =>
        {
            if (IsFocused)
            {
                if (Text.Length < MaxLength) // Печатные символы
                {
                    Text = Text.Insert(_cursorPosition, c.ToString());
                    _cursorPosition++;
                    OnTextChanged?.Invoke(Text);
                }
            }

        };
    }
    
    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        
        if (IsFocused)
        {
            HandleTextInput();
            
            // Мигание курсора
            _cursorBlinkTimer += deltaTime;
            if (_cursorBlinkTimer >= 0.5f)
            {
                _showCursor = !_showCursor;
                _cursorBlinkTimer = 0f;
            }
        }
    }
    
    private void HandleTextInput()
    {
        if (IsReadOnly) return;
        
        // Обработка специальных клавиш
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _cursorPosition > 0)
        {
            Text = Text.Remove(_cursorPosition - 1, 1);
            _cursorPosition--;
            OnTextChanged?.Invoke(Text);
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.Delete) && _cursorPosition < Text.Length)
        {
            Text = Text.Remove(_cursorPosition, 1);
            OnTextChanged?.Invoke(Text);
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.Left) && _cursorPosition > 0)
        {
            _cursorPosition--;
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.Right) && _cursorPosition < Text.Length)
        {
            _cursorPosition++;
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.Home))
        {
            _cursorPosition = 0;
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.End))
        {
            _cursorPosition = Text.Length;
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            OnEnterPressed?.Invoke();
        }
    }
    
    protected override void RenderSelf()
    {
        base.RenderSelf();
        
        var displayText = string.IsNullOrEmpty(Text) ? Placeholder : Text;
        
        if (!string.IsNullOrEmpty(displayText))
        {
            // Обрезаем текст если он не помещается
            var availableWidth = Size.X - ResolvedStyle.Padding.Left - ResolvedStyle.Padding.Right;
            var clippedText = ClipText(displayText, availableWidth);
            
            DrawText(clippedText, TextAlign.Left);
        }
        
        // Рендерим курсор если элемент в фокусе
        if (IsFocused && _showCursor && !IsReadOnly)
        {
            var cursorText = Text.Substring(0, Math.Min(_cursorPosition, Text.Length));
            var cursorX = Position.X + ResolvedStyle.Padding.Left + Raylib.MeasureText(cursorText, ResolvedStyle.FontSize);
            var cursorY = Position.Y + ResolvedStyle.Padding.Top;
            var cursorHeight = Size.Y - ResolvedStyle.Padding.Top - ResolvedStyle.Padding.Bottom;
            
            Raylib.DrawRectangle((int)cursorX, (int)cursorY, 2, (int)cursorHeight, ResolvedStyle.TextColor);
        }
    }
    
    private string ClipText(string text, float maxWidth)
    {
        if (Raylib.MeasureText(text, ResolvedStyle.FontSize) <= maxWidth)
            return text;
            
        for (int i = text.Length - 1; i >= 0; i--)
        {
            var substring = text.Substring(0, i) + "...";
            if (Raylib.MeasureText(substring, ResolvedStyle.FontSize) <= maxWidth)
                return substring;
        }
        
        return "...";
    }
}
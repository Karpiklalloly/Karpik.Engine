using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class ProgressBar : VisualElement
{
    private float _value;
    private float _maxValue = 100f;

    // --- Свойства для управления прогрессом ---
    public float MaxValue
    {
        get => _maxValue;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "MaxValue must be positive.");
            _maxValue = value;
            // Пересчитываем значение, чтобы оно осталось в пределах нового максимума
            Value = _value; // Используем сеттер Value для проверки
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            // Ограничиваем значение диапазоном [0, MaxValue]
            _value = Math.Clamp(value, 0f, _maxValue);
            string textToShow = string.Format(TextFormat, NormalizedValue * 100f);
            _text.Text = textToShow;
            int foregroundWidth = (int)(Bounds.Width * NormalizedValue);
            _foregroundPanel.Size = new Vector2(foregroundWidth, Bounds.Height);
        }
    }

    /// <summary>
    /// Прогресс в диапазоне от 0.0 до 1.0
    /// </summary>
    public float NormalizedValue => (_maxValue > 0) ? (_value / _maxValue) : 0f;

    // --- Свойства для внешнего вида ---
    public Color BackgroundColor
    {
        get => _backPanel.Color;
        set => _backPanel.Color = value;
    }

    public Color ForegroundColor
    {
        get => _foregroundPanel.Color;
        set => _foregroundPanel.Color = value;
    }

    public Texture2D BackgroundTexture
    {
        get => _backPanel.Texture;
        set => _backPanel.Texture = value;
    }

    public Texture2D ForegroundTexture
    {
        get => _foregroundPanel.Texture;
        set => _foregroundPanel.Texture = value;
    }

    // --- Свойства для текста (Опционально) ---
    public bool ShowText
    {
        get => _text.IsVisible;
        set => _text.IsVisible = value;
    }

    public Font Font
    {
        get => _text.Font;
        set => _text.Font = value;
    }

    public Color TextColor
    {
        get =>  _text.Color;
        set => _text.Color = value;
    }
    public string TextFormat { get; set; } = "{0:0}%"; // Формат для string.Format (0 - значение 0-100)

    private Panel _backPanel;
    private Panel _foregroundPanel;
    private Label _text;

    // --- Конструктор ---
    public ProgressBar(Vector2 size) : base(size)
    {
        _backPanel = new Panel(size);
        _backPanel.Color = Color.Gray;
        _foregroundPanel = new Panel(size);
        _foregroundPanel.Color = Color.Green;
        _text = new Label(string.Empty);
        // ProgressBar обычно не интерактивен
        IsEnabled = false;
        Add(_backPanel);
        Add(_foregroundPanel);
        Add(_text);
    }

    protected override void DrawSelf()
    {
        
        if (_value > 0) // Рисуем только если есть прогресс
        {
            // Рассчитываем ширину заполненной части

            // int foregroundWidth = (int)(Bounds.Width * NormalizedValue);
            // if (foregroundWidth > 0)
            // {
            //     Rectangle foregroundRect = new Rectangle(Bounds.X, Bounds.Y, foregroundWidth, Bounds.Height);
            //
            //     if (ForegroundTexture != null)
            //     {
            //         // Рисуем часть текстуры заполнения
            //         // Рассчитываем SourceRectangle для тайлинга или обрезки текстуры
            //         Rectangle sourceRect = new Rectangle(0, 0,
            //             (int)(ForegroundTexture.Width * NormalizedValue), // Берем часть ширины текстуры
            //             ForegroundTexture.Height);
            //         spriteBatch.Draw(ForegroundTexture, foregroundRect, sourceRect, Color.White);
            //         // Примечание: Этот способ растянет/сожмет текстуру. Для тайлинга нужен другой подход.
            //     }
            //     else
            //     {
            //         // Заливка цветом
            //         spriteBatch.Draw(pixel, foregroundRect, ForegroundColor);
            //     }
            // }
        }
    }
}
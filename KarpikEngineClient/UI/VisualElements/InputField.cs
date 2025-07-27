using System.Numerics;
using System.Text;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class InputField : VisualElement
{
    public string Text
    {
        get => _currentText;
        set
        {
            _textBuilder.Clear();
            _textBuilder.Append(value);
            _currentText = value;
            _caretPosition = value.Length; // Устанавливаем курсор в конец текста
            Invalidate();
        }
    }

    public Font Font { get; set; } = UI.DefaultFont;
    public Color TextColor { get; set; } = Color.Black;
    public Color BackgroundColor { get; set; } = Color.White;
    public Color FocusedBackgroundColor { get; set; } = Color.Yellow;
    public Color CaretColor { get; set; } = Color.Black;
    public int MaxLength { get; set; } = 128; // Ограничение длины
    
    public event Action<string> TextChanged;
    public event Action EnterPressed;
    
    public bool IsFocused { get; private set; } = false;

    private StringBuilder _textBuilder = new StringBuilder();
    private string _currentText = "";
    private int _caretPosition = 0; // Позиция курсора
    private double _caretTimer = 0f; // Таймер для мигания
    private const float CaretBlinkRate = 0.5f; // Секунд на мигание

    private Panel _panel;
    
    // Статическое поле для отслеживания фокуса (только одно поле может быть в фокусе)
    private static InputField _currentlyFocusedField = null;
    
    public InputField(Vector2 size) : base(size)
    {
        _panel = new Panel(size);
        // --- Подписка на событие ввода текста ---
        Input.CharPressed += HandleTextInput;
    }
    
    // Важно отписаться при удалении объекта, чтобы избежать утечек!
    public void Unsubscribe()
    {
        Input.CharPressed -= HandleTextInput;
    }

    private void HandleTextInput(char key)
    {
        if (!IsFocused || !IsEnabled) return; // Обрабатываем только если в фокусе
        
        // Проверяем, можно ли добавить символ
        
        if (_textBuilder.Length < MaxLength)
        {
            // Вставляем символ в позицию курсора
             _textBuilder.Insert(_caretPosition, key);
             _caretPosition++;
             OnTextChanged();
        }
    }


    protected override void HandleInput()
    {
        if (!IsEnabled)
        {
            if (IsFocused)
            {
                LoseFocus(); // Теряем фокус, если отключили
            }
            return;
        }

        if (Input.IsMouseLeftButtonDown) // Проверяем клик
        {
            if (IsHovered)
            {
                GainFocus();
                // TODO: Установить позицию курсора по клику мыши (требует расчета по символам)
                _caretPosition = _textBuilder.Length; // Пока ставим в конец
            }
            else if (IsFocused)
            {
                LoseFocus(); // Кликнули вне поля - теряем фокус
            }
        }

        if (IsFocused)
        {
            // Обработка управляющих клавиш
            if (Input.IsPressed(KeyboardKey.Back) && _caretPosition > 0)
            {
                _textBuilder.Remove(_caretPosition - 1, 1);
                _caretPosition--;
                OnTextChanged();
            }
            if (Input.IsPressed(KeyboardKey.Delete) && _caretPosition < _textBuilder.Length)
            {
                 _textBuilder.Remove(_caretPosition, 1);
                 OnTextChanged();
                 // Позиция курсора не меняется
            }
            if (Input.IsPressed(KeyboardKey.Left) && _caretPosition > 0)
            {
                _caretPosition--;
                ResetCaretBlink();
            }
            if (Input.IsPressed(KeyboardKey.Right) && _caretPosition < _textBuilder.Length)
            {
                _caretPosition++;
                ResetCaretBlink();
            }
            if (Input.IsPressed(KeyboardKey.Home))
            {
                 _caretPosition = 0;
                 ResetCaretBlink();
            }
            if (Input.IsPressed(KeyboardKey.End))
            {
                 _caretPosition = _textBuilder.Length;
                 ResetCaretBlink();
            }
            if (Input.IsPressed(KeyboardKey.Enter))
            {
                 EnterPressed?.Invoke();
                 Invalidate();
                 // LoseFocus(); // Опционально: терять фокус по Enter
            }
             // TODO: Выделение текста, копирование, вставка
        }
    }

    private void GainFocus()
    {
        if (IsFocused) return;
        // Убираем фокус с предыдущего поля, если оно было
        _currentlyFocusedField?.LoseFocus();

        IsFocused = true;
        _currentlyFocusedField = this;
        ResetCaretBlink();
        Invalidate();
    }

    private void LoseFocus()
    {
        if (!IsFocused) return;
        IsFocused = false;
        if (_currentlyFocusedField == this)
        {
            _currentlyFocusedField = null;
        }
        Invalidate();
    }

    private void OnTextChanged()
    {
        Text = _textBuilder.ToString(); // Обновляем публичное свойство
        TextChanged?.Invoke(Text);
        ResetCaretBlink();
        Invalidate();
    }

    private void ResetCaretBlink()
    {
         _caretTimer = 0f; // Сбрасываем таймер, чтобы курсор сразу стал видимым
         Invalidate();
    }

    protected override void OnUpdate(double deltaTime)
    {
        // Обновляем таймер курсора
        if (IsFocused)
        {
            _caretTimer += deltaTime;
        }
    }

    protected override void DrawSelf()
    {
        _panel.Color = IsFocused ? FocusedBackgroundColor : BackgroundColor;
        var uiBounds = Bounds;

        // Рисуем текст
        if (!Raylib.IsFontValid(Font)) return;
        
        // Простая отрисовка без скроллинга/клиппинга
        // TODO: Добавить клиппинг текста, если он выходит за Bounds
        var scale = 0.9f * Size.Y;
        var textPosition = Utils.GetTextPosition(Bounds, Size, new Vector2(0, 0.5f));
        Utils.DrawText(Font, _textBuilder.ToString(), textPosition, scale, TextColor);

        // Рисуем курсор (мигающий)
        if (IsFocused && (_caretTimer % (CaretBlinkRate * 2)) < CaretBlinkRate)
        {
            // Вычисляем позицию курсора
            string textBeforeCaret = _textBuilder.ToString(0, _caretPosition);
            Vector2 caretOffset = new Vector2(Raylib.MeasureText(textBeforeCaret, (int)scale));
            int caretX = (int)(textPosition.X + caretOffset.X);
            Rectangle caretRect = new Rectangle(caretX, uiBounds.Y + 4, 1, uiBounds.Height - 8); // Тонкий курсор
            Utils.DrawRectangle(caretRect, CaretColor);
        }
    }
}
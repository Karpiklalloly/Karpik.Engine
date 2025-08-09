using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class InputManager
{
    private readonly LayerManager _layerManager;
    private readonly List<InputEvent> _currentFrameEvents = new();
    
    // Состояние мыши для отслеживания кликов
    private Vector2 _lastMousePosition;
    private readonly Dictionary<MouseButton, bool> _mouseButtonStates = new();
    private readonly Dictionary<MouseButton, bool> _mouseButtonPressed = new();
    private readonly Dictionary<MouseButton, bool> _mouseButtonReleased = new();
    
    // Состояние клавиатуры
    private readonly Dictionary<KeyboardKey, bool> _keyStates = new();
    private readonly Dictionary<KeyboardKey, bool> _keyPressed = new();
    private readonly Dictionary<KeyboardKey, bool> _keyReleased = new();
    
    public InputManager(LayerManager layerManager)
    {
        _layerManager = layerManager;
    }
    
    public void Update()
    {
        _currentFrameEvents.Clear();
        
        // Обновляем состояние мыши
        UpdateMouseInput();
        
        // Обновляем состояние клавиатуры
        UpdateKeyboardInput();
        
        // Обрабатываем события
        ProcessEvents();
    }
    
    private void UpdateMouseInput()
    {
        var currentMousePos = Raylib.GetMousePosition();
        
        // Проверяем движение мыши
        if (currentMousePos != _lastMousePosition)
        {
            _currentFrameEvents.Add(InputEvent.MouseMove(currentMousePos));
            _lastMousePosition = currentMousePos;
        }
        
        // Проверяем кнопки мыши
        var mouseButtons = new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle };
        
        foreach (var button in mouseButtons)
        {
            bool currentState = Raylib.IsMouseButtonDown(button);
            bool wasPressed = _mouseButtonStates.GetValueOrDefault(button, false);
            
            _mouseButtonPressed[button] = !wasPressed && currentState;
            _mouseButtonReleased[button] = wasPressed && !currentState;
            _mouseButtonStates[button] = currentState;
            
            if (_mouseButtonPressed[button])
            {
                _currentFrameEvents.Add(InputEvent.MouseDown(currentMousePos, button));
            }
            
            if (_mouseButtonReleased[button])
            {
                _currentFrameEvents.Add(InputEvent.MouseUp(currentMousePos, button));
                // Клик = нажатие и отпускание в одном месте
                _currentFrameEvents.Add(InputEvent.MouseClick(currentMousePos, button));
            }
        }
    }
    
    private void UpdateKeyboardInput()
    {
        // Проверяем основные клавиши
        var keys = new[]
        {
            KeyboardKey.Space, KeyboardKey.Enter, KeyboardKey.Escape, KeyboardKey.Tab,
            KeyboardKey.Backspace, KeyboardKey.Delete, KeyboardKey.Left, KeyboardKey.Right,
            KeyboardKey.Up, KeyboardKey.Down, KeyboardKey.Home, KeyboardKey.End
        };
        
        foreach (var key in keys)
        {
            bool currentState = Raylib.IsKeyDown(key);
            bool wasPressed = _keyStates.GetValueOrDefault(key, false);
            
            _keyPressed[key] = !wasPressed && currentState;
            _keyReleased[key] = wasPressed && !currentState;
            _keyStates[key] = currentState;
            
            if (_keyPressed[key])
            {
                _currentFrameEvents.Add(InputEvent.KeyDown(key));
            }
            
            if (_keyReleased[key])
            {
                _currentFrameEvents.Add(InputEvent.KeyUp(key));
            }
        }
        
        // Обрабатываем ввод текста
        int charPressed = Raylib.GetCharPressed();
        while (charPressed > 0)
        {
            _currentFrameEvents.Add(InputEvent.TextInput((char)charPressed));
            charPressed = Raylib.GetCharPressed();
        }
    }
    
    private void ProcessEvents()
    {
        foreach (var inputEvent in _currentFrameEvents)
        {
            if (inputEvent.Handled) continue;
            
            // Отладочная информация для кликов мыши
            if (inputEvent.Type == InputEventType.MouseClick)
            {
                Console.WriteLine($"InputManager: Processing MouseClick at {inputEvent.MousePosition}");
            }
            
            // Передаем события слоям в порядке приоритета (сверху вниз)
            for (int i = _layerManager.Layers.Count - 1; i >= 0; i--)
            {
                var layer = _layerManager.Layers[i];
                Console.WriteLine($"InputManager: Sending event to layer {layer.Name} (visible: {layer.Visible}, interactive: {layer.Interactive})");
                
                if (layer.HandleInputEvent(inputEvent))
                {
                    Console.WriteLine($"InputManager: Event handled by layer {layer.Name}");
                    inputEvent.Handled = true;
                    break; // Событие обработано, прекращаем передачу
                }
            }
            
            if (!inputEvent.Handled && inputEvent.Type == InputEventType.MouseClick)
            {
                Console.WriteLine("InputManager: MouseClick event was not handled by any layer");
            }
        }
    }
    
    // Методы для получения состояния ввода (для совместимости)
    public bool IsMouseButtonPressed(MouseButton button)
    {
        return _mouseButtonPressed.GetValueOrDefault(button, false);
    }
    
    public bool IsMouseButtonReleased(MouseButton button)
    {
        return _mouseButtonReleased.GetValueOrDefault(button, false);
    }
    
    public bool IsMouseButtonDown(MouseButton button)
    {
        return _mouseButtonStates.GetValueOrDefault(button, false);
    }
    
    public bool IsKeyPressed(KeyboardKey key)
    {
        return _keyPressed.GetValueOrDefault(key, false);
    }
    
    public bool IsKeyReleased(KeyboardKey key)
    {
        return _keyReleased.GetValueOrDefault(key, false);
    }
    
    public bool IsKeyDown(KeyboardKey key)
    {
        return _keyStates.GetValueOrDefault(key, false);
    }
    
    public Vector2 GetMousePosition()
    {
        return _lastMousePosition;
    }
}
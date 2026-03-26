namespace Karpik.Engine.Client.UI.Core;

public class InputState
{
    public Vector2 MousePosition;
    public Vector2 PreviousMousePosition;
    public bool IsMouseDown;
    public bool WasMouseDown;
    public int? FocusedWidgetIndex;
    public int? HoveredWidgetIndex;
    public int? PressedWidgetIndex;
}

public class InputSystem
{
    private readonly WidgetStorage _storage;
    private readonly WidgetTree _tree;
    private readonly EventDispatcher _events;
    private readonly InputState _state = new();

    public InputState State => _state;

    public InputSystem(WidgetStorage storage, WidgetTree tree, EventDispatcher events)
    {
        _storage = storage;
        _tree = tree;
        _events = events;
    }

    public void Update(Vector2 mousePosition, bool isMouseDown, int rootWidgetIndex)
    {
        _state.PreviousMousePosition = _state.MousePosition;
        _state.MousePosition = mousePosition;
        _state.WasMouseDown = _state.IsMouseDown;
        _state.IsMouseDown = isMouseDown;

        ProcessHitTest(rootWidgetIndex);
        ProcessInteractions();
    }

    private void ProcessHitTest(int rootWidgetIndex)
    {
        int? previousHovered = _state.HoveredWidgetIndex;

        int found = _tree.FindWidgetAt(rootWidgetIndex, _state.MousePosition);
        
        if (found >= 0)
        {
            var widget = _storage.GetWidget(found);
            if (!widget.IsEnabled)
            {
                found = -1;
            }
        }
        
        _state.HoveredWidgetIndex = found >= 0 ? found : null;

        if (previousHovered != _state.HoveredWidgetIndex)
        {
            if (previousHovered.HasValue)
            {
                _events.DispatchUnhover(previousHovered.Value);
            }

            if (_state.HoveredWidgetIndex.HasValue)
            {
                _events.DispatchHover(_state.HoveredWidgetIndex.Value);
            }
        }
    }

    private void ProcessInteractions()
    {
        if (_state.IsMouseDown && !_state.WasMouseDown && _state.HoveredWidgetIndex.HasValue)
        {
            _state.PressedWidgetIndex = _state.HoveredWidgetIndex;
            _events.DispatchPress(_state.PressedWidgetIndex.Value);
        }

        if (!_state.IsMouseDown && _state.WasMouseDown && _state.PressedWidgetIndex.HasValue)
        {
            _events.DispatchRelease(_state.PressedWidgetIndex.Value);

            if (_state.HoveredWidgetIndex == _state.PressedWidgetIndex)
            {
                _events.DispatchClick(_state.PressedWidgetIndex.Value);
            }

            _state.PressedWidgetIndex = null;
        }
    }

    public void SetFocus(int? widgetIndex)
    {
        int? previousFocus = _state.FocusedWidgetIndex;

        if (previousFocus.HasValue && _storage.Has(previousFocus.Value))
        {
            _events.DispatchBlur(previousFocus.Value);
        }

        _state.FocusedWidgetIndex = widgetIndex;

        if (widgetIndex.HasValue && _storage.Has(widgetIndex.Value))
        {
            _events.DispatchFocus(widgetIndex.Value);
        }
    }

    public void ClearFocus()
    {
        SetFocus(null);
    }

    public bool IsFocused(int widgetIndex) => _state.FocusedWidgetIndex == widgetIndex;
    public bool IsHovered(int widgetIndex) => _state.HoveredWidgetIndex == widgetIndex;
    public bool IsPressed(int widgetIndex) => _state.PressedWidgetIndex == widgetIndex;
}

public static class HitTest
{
    public static int FindWidgetAt(WidgetStorage storage, int rootIndex, Vector2 position)
    {
        var tree = new WidgetTree(storage);
        return tree.FindWidgetAt(rootIndex, position);
    }

    public static int FindWidgetAtRecursive(WidgetStorage storage, int rootIndex, Vector2 position)
    {
        var widgets = new List<(int index, int zIndex)>();
        CollectVisibleWidgets(storage, rootIndex, position, widgets);
        widgets.Sort((a, b) => b.zIndex.CompareTo(a.zIndex));

        foreach (var (index, _) in widgets)
        {
            var widget = storage.GetWidget(index);
            if (widget.Bounds.Contains(position))
            {
                return index;
            }
        }

        return -1;
    }

    private static void CollectVisibleWidgets(WidgetStorage storage, int index, Vector2 position, List<(int, int)> result)
    {
        if (!storage.Has(index))
            return;

        var widget = storage.GetWidget(index);
        if (widget.IsVisible)
        {
            result.Add((index, widget.ZIndex));

            if (widget.HasChildren)
            {
                var childIndex = widget.FirstChildIndex;
                while (childIndex != UIWidget.NoChild)
                {
                    CollectVisibleWidgets(storage, childIndex, position, result);
                    childIndex = storage.GetWidget(childIndex).NextSiblingIndex;
                }
            }
        }
    }
}

public class FocusManager
{
    private readonly WidgetStorage _storage;
    private readonly InputSystem _inputSystem;
    private readonly List<int> _focusableIndices = new();

    public FocusManager(WidgetStorage storage, InputSystem inputSystem)
    {
        _storage = storage;
        _inputSystem = inputSystem;
    }

    public void RegisterFocusable(int widgetIndex)
    {
        if (!_focusableIndices.Contains(widgetIndex))
        {
            _focusableIndices.Add(widgetIndex);
        }
    }

    public void UnregisterFocusable(int widgetIndex)
    {
        _focusableIndices.Remove(widgetIndex);
    }

    public void NavigateNext()
    {
        if (_focusableIndices.Count == 0)
            return;

        int currentIndex = _inputSystem.State.FocusedWidgetIndex.HasValue
            ? _focusableIndices.IndexOf(_inputSystem.State.FocusedWidgetIndex.Value)
            : -1;

        int nextIndex = (currentIndex + 1) % _focusableIndices.Count;
        _inputSystem.SetFocus(_focusableIndices[nextIndex]);
    }

    public void NavigatePrevious()
    {
        if (_focusableIndices.Count == 0)
            return;

        int currentIndex = _inputSystem.State.FocusedWidgetIndex.HasValue
            ? _focusableIndices.IndexOf(_inputSystem.State.FocusedWidgetIndex.Value)
            : 0;

        int prevIndex = currentIndex - 1;
        if (prevIndex < 0)
            prevIndex = _focusableIndices.Count - 1;

        _inputSystem.SetFocus(_focusableIndices[prevIndex]);
    }
}

namespace Karpik.Engine.Client.UI.Core;

public class WidgetEvents
{
    private readonly WidgetStorage _storage;
    private readonly Dictionary<int, EventHandlers> _handlers;

    public WidgetEvents(WidgetStorage storage)
    {
        _storage = storage;
        _handlers = new Dictionary<int, EventHandlers>();
    }

    public EventHandlers GetOrCreate(int index)
    {
        if (!_handlers.TryGetValue(index, out var handlers))
        {
            handlers = new EventHandlers();
            _handlers[index] = handlers;
        }
        return handlers;
    }

    public bool HasHandlers(int index) => _handlers.ContainsKey(index);

    public void Remove(int index)
    {
        _handlers.Remove(index);
    }

    public void Clear()
    {
        _handlers.Clear();
    }
}

public class EventHandlers
{
    public Action<int>? OnClick;
    public Action<int>? OnHover;
    public Action<int>? OnUnhover;
    public Action<int>? OnPress;
    public Action<int>? OnRelease;
    public Action<int>? OnFocus;
    public Action<int>? OnBlur;
    public Action<int>? OnKeyDown;
    public Action<int>? OnKeyUp;
    public Action<int, char>? OnCharInput;

    public void Clear()
    {
        OnClick = null;
        OnHover = null;
        OnUnhover = null;
        OnPress = null;
        OnRelease = null;
        OnFocus = null;
        OnBlur = null;
        OnKeyDown = null;
        OnKeyUp = null;
        OnCharInput = null;
    }
}

public class EventDispatcher
{
    private readonly WidgetStorage _storage;
    private readonly WidgetEvents _events;

    public EventDispatcher(WidgetStorage storage, WidgetEvents events)
    {
        _storage = storage;
        _events = events;
    }

    public void DispatchClick(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        var widget = _storage.GetWidget(widgetIndex);
        if (!widget.IsEnabled)
            return;

        if (_events.HasHandlers(widgetIndex))
        {
            var handlers = _events.GetOrCreate(widgetIndex);
            handlers.OnClick?.Invoke(widgetIndex);
        }

        if (widget.BubbleEvents)
        {
            BubbleEvent(widgetIndex, "OnClick");
        }
    }

    public void DispatchHover(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        var widget = _storage.GetWidget(widgetIndex);
        if (!widget.IsEnabled)
            return;

        if (widget.State != InteractionState.Hovered)
        {
            ref var w = ref _storage.Get(widgetIndex);
            w.State = InteractionState.Hovered;

            if (_events.HasHandlers(widgetIndex))
            {
                var handlers = _events.GetOrCreate(widgetIndex);
                handlers.OnHover?.Invoke(widgetIndex);
            }

            if (w.BubbleEvents)
            {
                BubbleEvent(widgetIndex, "OnHover");
            }
        }
    }

    public void DispatchUnhover(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        ref var widget = ref _storage.Get(widgetIndex);

        if (widget.State == InteractionState.Hovered)
        {
            widget.State = InteractionState.Normal;

            if (_events.HasHandlers(widgetIndex))
            {
                var handlers = _events.GetOrCreate(widgetIndex);
                handlers.OnUnhover?.Invoke(widgetIndex);
            }

            if (widget.BubbleEvents)
            {
                BubbleEvent(widgetIndex, "OnUnhover");
            }
        }
    }

    public void DispatchPress(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        ref var widget = ref _storage.Get(widgetIndex);
        if (!widget.IsEnabled)
            return;

        widget.State = InteractionState.Pressed;

        if (_events.HasHandlers(widgetIndex))
        {
            var handlers = _events.GetOrCreate(widgetIndex);
            handlers.OnPress?.Invoke(widgetIndex);
        }

        if (widget.BubbleEvents)
        {
            BubbleEvent(widgetIndex, "OnPress");
        }
    }

    public void DispatchRelease(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        ref var widget = ref _storage.Get(widgetIndex);
        widget.State = InteractionState.Hovered;

        if (_events.HasHandlers(widgetIndex))
        {
            var handlers = _events.GetOrCreate(widgetIndex);
            handlers.OnRelease?.Invoke(widgetIndex);
        }

        if (widget.BubbleEvents)
        {
            BubbleEvent(widgetIndex, "OnRelease");
        }
    }

    public void DispatchFocus(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        ref var widget = ref _storage.Get(widgetIndex);
        widget.State = InteractionState.Focused;

        if (_events.HasHandlers(widgetIndex))
        {
            var handlers = _events.GetOrCreate(widgetIndex);
            handlers.OnFocus?.Invoke(widgetIndex);
        }
    }

    public void DispatchBlur(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        ref var widget = ref _storage.Get(widgetIndex);
        if (widget.State == InteractionState.Focused)
        {
            widget.State = InteractionState.Normal;
        }

        if (_events.HasHandlers(widgetIndex))
        {
            var handlers = _events.GetOrCreate(widgetIndex);
            handlers.OnBlur?.Invoke(widgetIndex);
        }
    }

    private void BubbleEvent(int startIndex, string eventType)
    {
        var widget = _storage.GetWidget(startIndex);

        if (!widget.BubbleEvents)
            return;

        while (widget.HasParent)
        {
            int parentIndex = widget.ParentIndex;
            if (!_storage.Has(parentIndex))
                break;

            widget = _storage.GetWidget(parentIndex);
            
            if (!widget.BubbleEvents)
                break;

            if (_events.HasHandlers(parentIndex))
            {
                var handlers = _events.GetOrCreate(parentIndex);
                switch (eventType)
                {
                    case "OnClick":
                        handlers.OnClick?.Invoke(parentIndex);
                        break;
                    case "OnHover":
                        handlers.OnHover?.Invoke(parentIndex);
                        break;
                    case "OnUnhover":
                        handlers.OnUnhover?.Invoke(parentIndex);
                        break;
                    case "OnPress":
                        handlers.OnPress?.Invoke(parentIndex);
                        break;
                    case "OnRelease":
                        handlers.OnRelease?.Invoke(parentIndex);
                        break;
                }
            }
        }
    }
}

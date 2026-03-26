using Xunit;
using Karpik.Engine.Client.UI.Core;

namespace GameUI.Core.Tests;

public class EventsTests
{
    [Fact]
    public void DispatchClick_CallsOnClickHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool clicked = false;
        handlers.OnClick += _ => clicked = true;

        dispatcher.DispatchClick(index);

        Assert.True(clicked);
    }

    [Fact]
    public void DispatchClick_DisabledWidget_DoesNotCallHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = false };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool clicked = false;
        handlers.OnClick += _ => clicked = true;

        dispatcher.DispatchClick(index);

        Assert.False(clicked);
    }

    [Fact]
    public void DispatchHover_ChangesStateToHovered()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        dispatcher.DispatchHover(index);

        Assert.Equal(InteractionState.Hovered, storage.Get(index).State);
    }

    [Fact]
    public void BubbleEvents_PropagatesToParent()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var parent = new UIWidget(UiTypeId.Window) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 200, 200) };
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50) };
        var childIndex = storage.AddChild(parentIndex, child);

        // Both parent and child need BubbleEvents = true
        ref var parentWidget = ref storage.Get(parentIndex);
        parentWidget.BubbleEvents = true;
        
        ref var childWidget = ref storage.Get(childIndex);
        childWidget.BubbleEvents = true;

        var parentHandlers = events.GetOrCreate(parentIndex);
        bool parentClicked = false;
        parentHandlers.OnClick += _ => parentClicked = true;

        dispatcher.DispatchClick(childIndex);

        Assert.True(parentClicked);
    }

    [Fact]
    public void MultipleSubscribers_AllCalled()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool called1 = false;
        bool called2 = false;
        bool called3 = false;

        handlers.OnClick += _ => called1 = true;
        handlers.OnClick += _ => called2 = true;
        handlers.OnClick += _ => called3 = true;

        dispatcher.DispatchClick(index);

        Assert.True(called1);
        Assert.True(called2);
        Assert.True(called3);
    }

    [Fact]
    public void Unsubscribe_RemovesHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool called = false;
        Action<int> handler = _ => called = true;
        
        handlers.OnClick += handler;
        dispatcher.DispatchClick(index);
        Assert.True(called);

        called = false;
        handlers.OnClick -= handler;
        dispatcher.DispatchClick(index);
        Assert.False(called);
    }

    [Fact]
    public void DispatchHover_CallsOnHoverHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool hovered = false;
        handlers.OnHover += _ => hovered = true;

        dispatcher.DispatchHover(index);

        Assert.True(hovered);
    }

    [Fact]
    public void DispatchUnhover_CallsOnUnhoverHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        dispatcher.DispatchHover(index);

        var handlers = events.GetOrCreate(index);
        bool unhovered = false;
        handlers.OnUnhover += _ => unhovered = true;

        dispatcher.DispatchUnhover(index);

        Assert.True(unhovered);
    }

    [Fact]
    public void DispatchPress_CallsOnPressHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool pressed = false;
        handlers.OnPress += _ => pressed = true;

        dispatcher.DispatchPress(index);

        Assert.True(pressed);
    }

    [Fact]
    public void DispatchRelease_CallsOnReleaseHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool released = false;
        handlers.OnRelease += _ => released = true;

        dispatcher.DispatchRelease(index);

        Assert.True(released);
    }

    [Fact]
    public void DispatchFocus_CallsOnFocusHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool focused = false;
        handlers.OnFocus += _ => focused = true;

        dispatcher.DispatchFocus(index);

        Assert.True(focused);
    }

    [Fact]
    public void DispatchBlur_CallsOnBlurHandler()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        dispatcher.DispatchFocus(index);

        var handlers = events.GetOrCreate(index);
        bool blurred = false;
        handlers.OnBlur += _ => blurred = true;

        dispatcher.DispatchBlur(index);

        Assert.True(blurred);
    }

    [Fact]
    public void EventHandlers_Clear_RemovesAllHandlers()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        handlers.OnClick += _ => { };
        handlers.OnHover += _ => { };
        handlers.OnPress += _ => { };

        handlers.Clear();

        Assert.Null(handlers.OnClick);
        Assert.Null(handlers.OnHover);
        Assert.Null(handlers.OnPress);
    }

    [Fact]
    public void WidgetEvents_Remove_RemovesHandlers()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        events.GetOrCreate(index);
        Assert.True(events.HasHandlers(index));

        events.Remove(index);
        Assert.False(events.HasHandlers(index));
    }

    [Fact]
    public void WidgetEvents_Clear_RemovesAllHandlers()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);

        var widget1 = new UIWidget(UiTypeId.Button);
        var widget2 = new UIWidget(UiTypeId.Label);
        
        events.GetOrCreate(storage.Add(widget1));
        events.GetOrCreate(storage.Add(widget2));

        Assert.True(events.HasHandlers(0));
        Assert.True(events.HasHandlers(1));

        events.Clear();

        Assert.False(events.HasHandlers(0));
        Assert.False(events.HasHandlers(1));
    }

    [Fact]
    public void Bubbling_MultiLevel_PropagatesToAllAncestors()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var grandParent = new UIWidget(UiTypeId.Window) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 300, 300) };
        var grandParentIndex = storage.Add(grandParent);

        var parent = new UIWidget(UiTypeId.Panel) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 200, 200) };
        var parentIndex = storage.AddChild(grandParentIndex, parent);

        var child = new UIWidget(UiTypeId.Button) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 100, 50) };
        var childIndex = storage.AddChild(parentIndex, child);

        var gpHandlers = events.GetOrCreate(grandParentIndex);
        var pHandlers = events.GetOrCreate(parentIndex);
        
        bool gpCalled = false;
        bool pCalled = false;
        
        gpHandlers.OnClick += _ => gpCalled = true;
        pHandlers.OnClick += _ => pCalled = true;

        dispatcher.DispatchClick(childIndex);

        Assert.True(pCalled);
        Assert.True(gpCalled);
    }

    [Fact]
    public void Bubbling_Stopped_WhenParentDoesNotBubble()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        // Parent does NOT bubble
        var parent = new UIWidget(UiTypeId.Window) { BubbleEvents = false, Bounds = new Rectangle(0, 0, 200, 200) };
        var parentIndex = storage.Add(parent);

        // Child DOES bubble - but parent stops it
        var child = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50) };
        var childIndex = storage.AddChild(parentIndex, child);
        
        // Child bubbles but parent doesn't
        ref var childWidget = ref storage.Get(childIndex);
        childWidget.BubbleEvents = true;
        
        ref var parentWidget = ref storage.Get(parentIndex);
        parentWidget.BubbleEvents = false;

        var parentHandlers = events.GetOrCreate(parentIndex);
        bool parentCalled = false;
        parentHandlers.OnClick += _ => parentCalled = true;

        dispatcher.DispatchClick(childIndex);

        Assert.False(parentCalled);
    }

    [Fact]
    public void DispatchClick_InvalidIndex_DoesNotThrow()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        dispatcher.DispatchClick(999);
        dispatcher.DispatchHover(999);
        dispatcher.DispatchPress(999);
    }

    [Fact]
    public void WidgetIndex_IsPassedToHandlers()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        int? receivedIndex = null;
        var handlers = events.GetOrCreate(index);
        handlers.OnClick += i => receivedIndex = i;

        dispatcher.DispatchClick(index);

        Assert.Equal(index, receivedIndex);
    }

    [Fact]
    public void HoverState_TransitionsCorrectly()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        Assert.Equal(InteractionState.Normal, storage.Get(index).State);

        dispatcher.DispatchHover(index);
        Assert.Equal(InteractionState.Hovered, storage.Get(index).State);

        dispatcher.DispatchPress(index);
        Assert.Equal(InteractionState.Pressed, storage.Get(index).State);

        dispatcher.DispatchRelease(index);
        Assert.Equal(InteractionState.Hovered, storage.Get(index).State);

        dispatcher.DispatchUnhover(index);
        Assert.Equal(InteractionState.Normal, storage.Get(index).State);
    }

    [Fact]
    public void FocusState_TransitionsCorrectly()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        Assert.Equal(InteractionState.Normal, storage.Get(index).State);

        dispatcher.DispatchFocus(index);
        Assert.Equal(InteractionState.Focused, storage.Get(index).State);

        dispatcher.DispatchBlur(index);
        Assert.Equal(InteractionState.Normal, storage.Get(index).State);
    }
}
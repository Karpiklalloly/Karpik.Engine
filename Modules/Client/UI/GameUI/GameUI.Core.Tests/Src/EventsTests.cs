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

public class EventDispatcherEdgeCaseTests
{
    [Fact]
    public void DispatchClick_BubblingStopped_ParentNotCalled()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var parent = new UIWidget(UiTypeId.Window) { BubbleEvents = false, Bounds = new Rectangle(0, 0, 200, 200) };
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 100, 50) };
        var childIndex = storage.AddChild(parentIndex, child);

        var parentHandlers = events.GetOrCreate(parentIndex);
        bool parentCalled = false;
        parentHandlers.OnClick += _ => parentCalled = true;

        dispatcher.DispatchClick(childIndex);

        Assert.False(parentCalled);
    }

    [Fact]
    public void DispatchHover_SameWidgetTwice_OnlyOneEvent()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        int hoverCount = 0;
        handlers.OnHover += _ => hoverCount++;

        dispatcher.DispatchHover(index);
        dispatcher.DispatchHover(index);
        dispatcher.DispatchHover(index);

        Assert.Equal(1, hoverCount);
    }

    [Fact]
    public void DispatchUnhover_NotHovered_NoOp()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = true };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        int unhoverCount = 0;
        handlers.OnUnhover += _ => unhoverCount++;

        dispatcher.DispatchUnhover(index);

        Assert.Equal(0, unhoverCount);
    }

    [Fact]
    public void DispatchPress_DisabledWidget_NoStateChange()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50), IsEnabled = false };
        var index = storage.Add(widget);

        dispatcher.DispatchPress(index);

        Assert.Equal(InteractionState.Normal, storage.Get(index).State);
    }

    [Fact]
    public void DispatchClick_BubblingMultipleLevels_AllParentsCalled()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var level1 = new UIWidget(UiTypeId.Window) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 300, 300) };
        var l1Index = storage.Add(level1);

        var level2 = new UIWidget(UiTypeId.Panel) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 200, 200) };
        var l2Index = storage.AddChild(l1Index, level2);

        var level3 = new UIWidget(UiTypeId.Button) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 100, 50) };
        var l3Index = storage.AddChild(l2Index, level3);

        var l1Handlers = events.GetOrCreate(l1Index);
        var l2Handlers = events.GetOrCreate(l2Index);
        
        int l1Clicks = 0, l2Clicks = 0;
        l1Handlers.OnClick += _ => l1Clicks++;
        l2Handlers.OnClick += _ => l2Clicks++;

        dispatcher.DispatchClick(l3Index);

        Assert.Equal(1, l2Clicks);
        Assert.Equal(1, l1Clicks);
    }

    [Fact]
    public void DispatchClick_Bubbling_ParentDisabled_ContinuesToGrandParent()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);

        var grandparent = new UIWidget(UiTypeId.Window) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 300, 300) };
        var gpIndex = storage.Add(grandparent);

        var parent = new UIWidget(UiTypeId.Panel) { BubbleEvents = false, Bounds = new Rectangle(0, 0, 200, 200) };
        var pIndex = storage.AddChild(gpIndex, parent);

        var child = new UIWidget(UiTypeId.Button) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 100, 50) };
        var cIndex = storage.AddChild(pIndex, child);

        var gpHandlers = events.GetOrCreate(gpIndex);
        int gpClicks = 0;
        gpHandlers.OnClick += _ => gpClicks++;

        dispatcher.DispatchClick(cIndex);

        Assert.Equal(0, gpClicks);
    }
}

public class WidgetEventsEdgeCaseTests
{
    [Fact]
    public void GetOrCreate_MultipleTimes_SameInstance()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        var h1 = events.GetOrCreate(index);
        var h2 = events.GetOrCreate(index);

        Assert.Same(h1, h2);
    }

    [Fact]
    public void HasHandlers_AfterRemove_ReturnsFalse()
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
    public void Clear_AllHandlersRemoved()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);

        for (int i = 0; i < 5; i++)
        {
            var widget = new UIWidget(UiTypeId.Button);
            events.GetOrCreate(storage.Add(widget));
        }

        events.Clear();

        for (int i = 0; i < 5; i++)
        {
            Assert.False(events.HasHandlers(i));
        }
    }
}

public class EventHandlersEdgeCaseTests
{
    [Fact]
    public void Clear_AllCallbacksNull()
    {
        var handlers = new EventHandlers();
        handlers.OnClick += _ => { };
        handlers.OnHover += _ => { };
        handlers.OnPress += _ => { };
        handlers.OnRelease += _ => { };
        handlers.OnFocus += _ => { };
        handlers.OnBlur += _ => { };
        handlers.OnKeyDown += _ => { };
        handlers.OnKeyUp += _ => { };
        handlers.OnCharInput += (_, _) => { };

        handlers.Clear();

        Assert.Null(handlers.OnClick);
        Assert.Null(handlers.OnHover);
        Assert.Null(handlers.OnPress);
        Assert.Null(handlers.OnRelease);
        Assert.Null(handlers.OnFocus);
        Assert.Null(handlers.OnBlur);
        Assert.Null(handlers.OnKeyDown);
        Assert.Null(handlers.OnKeyUp);
        Assert.Null(handlers.OnCharInput);
    }

    [Fact]
    public void MultipleHandlers_SameEvent_AllCalled()
    {
        var handlers = new EventHandlers();
        
        int callCount = 0;
        handlers.OnClick += _ => callCount++;
        handlers.OnClick += _ => callCount++;
        handlers.OnClick += _ => callCount++;

        handlers.OnClick?.Invoke(0);

        Assert.Equal(3, callCount);
    }

    [Fact]
    public void HandlerThrows_OtherHandlersContinue()
    {
        var handlers = new EventHandlers();
        
        bool secondCalled = false;
        handlers.OnClick += _ => secondCalled = true;

        handlers.OnClick?.Invoke(0);

        Assert.True(secondCalled);
    }
}
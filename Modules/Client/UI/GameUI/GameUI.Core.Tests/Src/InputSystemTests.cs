using Xunit;
using Karpik.Engine.Client.UI.Core;
using Vector2 = System.Numerics.Vector2;

namespace GameUI.Core.Tests;

public class InputSystemTests
{
    [Fact]
    public void Update_HoveredWidget_ChangesState()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index = storage.Add(widget);

        inputSystem.Update(new Vector2(50, 25), false, index);

        Assert.True(inputSystem.IsHovered(index));
    }

    [Fact]
    public void Update_MouseDown_PressesWidget()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index = storage.Add(widget);

        inputSystem.Update(new Vector2(50, 25), true, index);

        Assert.True(inputSystem.IsPressed(index));
    }

    [Fact]
    public void Update_MouseClick_DispatchesClick()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index = storage.Add(widget);

        var handlers = events.GetOrCreate(index);
        bool clicked = false;
        handlers.OnClick += _ => clicked = true;

        inputSystem.Update(new Vector2(50, 25), true, index);
        inputSystem.Update(new Vector2(50, 25), false, index);

        Assert.True(clicked);
    }

    [Fact]
    public void Update_MouseOutside_NoHover()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index = storage.Add(widget);

        inputSystem.Update(new Vector2(200, 200), false, index);

        Assert.False(inputSystem.IsHovered(index));
    }

    [Fact]
    public void SetFocus_ChangesFocusedWidget()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index = storage.Add(widget);

        inputSystem.SetFocus(index);

        Assert.True(inputSystem.IsFocused(index));
    }

    [Fact]
    public void SetFocus_Null_ClearsFocus()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index = storage.Add(widget);

        inputSystem.SetFocus(index);
        inputSystem.SetFocus(null);

        Assert.False(inputSystem.IsFocused(index));
    }

    [Fact]
    public void DisabledWidget_DoesNotReceiveInteractions()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = false,
            IsVisible = true
        };
        var index = storage.Add(widget);

        inputSystem.Update(new Vector2(50, 25), true, index);

        Assert.False(inputSystem.IsHovered(index));
        Assert.False(inputSystem.IsPressed(index));
    }

    [Fact]
    public void InvisibleWidget_DoesNotReceiveInteractions()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = false
        };
        var index = storage.Add(widget);

        inputSystem.Update(new Vector2(50, 25), false, index);

        Assert.False(inputSystem.IsHovered(index));
    }
}

public class HitTestTests
{
    [Fact]
    public void FindWidgetAt_PointInsideWidget_ReturnsIndex()
    {
        var storage = new WidgetStorage();

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            ZIndex = 0,
            IsVisible = true
        };
        var index = storage.Add(widget);

        var result = HitTest.FindWidgetAt(storage, index, new Vector2(50, 25));

        Assert.Equal(index, result);
    }

    [Fact]
    public void FindWidgetAt_PointOutsideWidget_ReturnsMinusOne()
    {
        var storage = new WidgetStorage();

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            ZIndex = 0,
            IsVisible = true
        };
        var index = storage.Add(widget);

        var result = HitTest.FindWidgetAt(storage, index, new Vector2(200, 200));

        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindWidgetAt_HigherZIndex_First()
    {
        var storage = new WidgetStorage();

        var widget1 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true
        };
        var index1 = storage.Add(widget1);

        var widget2 = new UIWidget(UiTypeId.Label)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 1,
            IsVisible = true
        };
        var index2 = storage.Add(widget2);

        var result = HitTest.FindWidgetAt(storage, index2, new Vector2(50, 50));

        Assert.Equal(index2, result);
    }

    [Fact]
    public void FindWidgetAt_InvisibleWidget_Skipped()
    {
        var storage = new WidgetStorage();

        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            ZIndex = 0,
            IsVisible = false
        };
        var index = storage.Add(widget);

        var result = HitTest.FindWidgetAt(storage, index, new Vector2(50, 25));

        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindWidgetAt_ChildWidget_Found()
    {
        var storage = new WidgetStorage();

        var parent = new UIWidget(UiTypeId.Window)
        {
            Bounds = new Rectangle(0, 0, 200, 200),
            ZIndex = 0,
            IsVisible = true
        };
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(50, 50, 100, 50),
            ZIndex = 1,
            IsVisible = true
        };
        var childIndex = storage.AddChild(parentIndex, child);

        var result = HitTest.FindWidgetAt(storage, parentIndex, new Vector2(75, 75));

        Assert.Equal(childIndex, result);
    }
}

public class FocusManagerTests
{
    [Fact]
    public void RegisterFocusable_AddsToList()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        focusManager.RegisterFocusable(index);

        focusManager.NavigateNext();

        Assert.True(inputSystem.IsFocused(index));
    }

    [Fact]
    public void NavigateNext_MovesToNextWidget()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget1 = new UIWidget(UiTypeId.Button);
        var widget2 = new UIWidget(UiTypeId.Label);
        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);

        focusManager.RegisterFocusable(index1);
        focusManager.RegisterFocusable(index2);

        inputSystem.SetFocus(index1);
        focusManager.NavigateNext();

        Assert.True(inputSystem.IsFocused(index2));
    }

    [Fact]
    public void NavigatePrevious_MovesToPreviousWidget()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget1 = new UIWidget(UiTypeId.Button);
        var widget2 = new UIWidget(UiTypeId.Label);
        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);

        focusManager.RegisterFocusable(index1);
        focusManager.RegisterFocusable(index2);

        inputSystem.SetFocus(index2);
        focusManager.NavigatePrevious();

        Assert.True(inputSystem.IsFocused(index1));
    }

    [Fact]
    public void UnregisterFocusable_RemovesFromList()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        focusManager.RegisterFocusable(index);
        focusManager.UnregisterFocusable(index);

        focusManager.NavigateNext();

        Assert.False(inputSystem.IsFocused(index));
    }
}

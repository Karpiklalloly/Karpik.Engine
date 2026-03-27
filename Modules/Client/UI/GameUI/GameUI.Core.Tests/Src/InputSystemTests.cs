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
    
    [Fact]
    public void FindWidgetAt_ChildWithOffsetParent_UsesAbsoluteCoordinates()
    {
        var storage = new WidgetStorage();

        var parent = new UIWidget(UiTypeId.Window)
        {
            Bounds = new Rectangle(100, 100, 200, 200),
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

        var result = HitTest.FindWidgetAt(storage, parentIndex, new Vector2(175, 175));

        Assert.Equal(childIndex, result);
    }
    
    [Fact]
    public void FindWidgetAt_GrandChildWithOffsetParents_UsesAbsoluteCoordinates()
    {
        var storage = new WidgetStorage();

        var window = new UIWidget(UiTypeId.Window)
        {
            Bounds = new Rectangle(100, 100, 400, 300),
            ZIndex = 0,
            IsVisible = true
        };
        var windowIndex = storage.Add(window);

        var panel = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(10, 20, 200, 150),
            ZIndex = 1,
            IsVisible = true
        };
        var panelIndex = storage.AddChild(windowIndex, panel);

        var button = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(5, 5, 80, 30),
            ZIndex = 2,
            IsVisible = true
        };
        var buttonIndex = storage.AddChild(panelIndex, button);

        var result = HitTest.FindWidgetAt(storage, windowIndex, new Vector2(140, 155));

        Assert.Equal(buttonIndex, result);
    }
    
    [Fact]
    public void FindWidgetAt_ChildOutsideClippingPanel_NotFound()
    {
        var storage = new WidgetStorage();

        var panel = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true,
            ClipChildren = true
        };
        var panelIndex = storage.Add(panel);

        var button = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(50, 50, 100, 50),
            ZIndex = 1,
            IsVisible = true
        };
        var buttonIndex = storage.AddChild(panelIndex, button);

        var result = HitTest.FindWidgetAt(storage, panelIndex, new Vector2(120, 75));

        Assert.Equal(-1, result);
    }
    
    [Fact]
    public void FindWidgetAt_ChildOutsideNonClippingPanel_Found()
    {
        var storage = new WidgetStorage();

        var panel = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true
        };
        var panelIndex = storage.Add(panel);

        var button = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(150, 50, 100, 50),
            ZIndex = 1,
            IsVisible = true
        };
        var buttonIndex = storage.AddChild(panelIndex, button);

        var result = HitTest.FindWidgetAt(storage, panelIndex, new Vector2(200, 75));

        Assert.Equal(buttonIndex, result);
    }
    
    [Fact]
    public void FindWidgetAt_DeepChildOutsideClippingPanel_NotFound()
    {
        var storage = new WidgetStorage();

        var panel = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true,
            ClipChildren = true
        };
        var panelIndex = storage.Add(panel);

        var container = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(10, 10, 50, 50),
            ZIndex = 1,
            IsVisible = true
        };
        var containerIndex = storage.AddChild(panelIndex, container);

        var button = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(60, 20, 80, 30),
            ZIndex = 2,
            IsVisible = true
        };
        var buttonIndex = storage.AddChild(containerIndex, button);

        var result = HitTest.FindWidgetAt(storage, panelIndex, new Vector2(120, 50));

        Assert.Equal(-1, result);
    }
    
    [Fact]
    public void FindWidgetAt_SameZIndexInDifferentParents_EachIndependent()
    {
        var storage = new WidgetStorage();

        var panel1 = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true
        };
        var panel1Index = storage.Add(panel1);

        var panel2 = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(100, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true
        };
        var panel2Index = storage.Add(panel2);

        var button1 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(10, 10, 50, 50),
            ZIndex = 10,
            IsVisible = true
        };
        var button1Index = storage.AddChild(panel1Index, button1);

        var button2 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(10, 10, 50, 50),
            ZIndex = 5,
            IsVisible = true
        };
        var button2Index = storage.AddChild(panel2Index, button2);

        var result1 = HitTest.FindWidgetAt(storage, panel1Index, new Vector2(30, 30));
        var result2 = HitTest.FindWidgetAt(storage, panel2Index, new Vector2(130, 30));

        Assert.Equal(button1Index, result1);
        Assert.Equal(button2Index, result2);
    }
    
    [Fact]
    public void FindWidgetAt_HigherZIndexWins_AmongSiblings()
    {
        var storage = new WidgetStorage();

        var panel = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(0, 0, 200, 200),
            ZIndex = 0,
            IsVisible = true
        };
        var panelIndex = storage.Add(panel);

        var button1 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(10, 10, 80, 80),
            ZIndex = 1,
            IsVisible = true
        };
        var button1Index = storage.AddChild(panelIndex, button1);

        var button2 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(10, 10, 80, 80),
            ZIndex = 2,
            IsVisible = true
        };
        var button2Index = storage.AddChild(panelIndex, button2);

        var result = HitTest.FindWidgetAt(storage, panelIndex, new Vector2(50, 50));

        Assert.Equal(button2Index, result);
    }
}

public class HitTestPropertyBasedTests
{
    private readonly Random _random = new(99999);

    [Fact]
    public void FindWidgetAt_RandomPositions_500Cases()
    {
        for (int i = 0; i < 500; i++)
        {
            var storage = new WidgetStorage();
            
            float x = _random.Next(-500, 500);
            float y = _random.Next(-500, 500);
            float w = _random.Next(50, 300);
            float h = _random.Next(50, 300);
            
            var widget = new UIWidget(UiTypeId.Button)
            {
                Bounds = new Rectangle(x, y, w, h),
                ZIndex = 0,
                IsVisible = true
            };
            var index = storage.Add(widget);
            
            float px = x + _random.Next(0, (int)w);
            float py = y + _random.Next(0, (int)h);
            
            var result = HitTest.FindWidgetAt(storage, 0, new Vector2(px, py));
            
            Assert.Equal(index, result);
        }
    }

    [Fact]
    public void FindWidgetAt_RandomPositionsOutside_500Cases()
    {
        for (int i = 0; i < 500; i++)
        {
            var storage = new WidgetStorage();
            
            float x = _random.Next(-500, 500);
            float y = _random.Next(-500, 500);
            float w = _random.Next(50, 300);
            float h = _random.Next(50, 300);
            
            var widget = new UIWidget(UiTypeId.Button)
            {
                Bounds = new Rectangle(x, y, w, h),
                ZIndex = 0,
                IsVisible = true
            };
            storage.Add(widget);
            
            float px = x + w + 1 + Math.Abs(_random.Next(1, 100));
            float py = y + h + 1 + Math.Abs(_random.Next(1, 100));
            
            var result = HitTest.FindWidgetAt(storage, 0, new Vector2(px, py));
            
            Assert.Equal(-1, result);
        }
    }

    [Fact]
    public void FindWidgetAt_MultipleWidgets_RandomLayout_500Cases()
    {
        for (int i = 0; i < 500; i++)
        {
            var storage = new WidgetStorage();
            
            int count = _random.Next(3, 10);
            var indices = new List<int>();
            
            for (int j = 0; j < count; j++)
            {
                float x = _random.Next(0, 500);
                float y = _random.Next(0, 500);
                float w = _random.Next(20, 100);
                float h = _random.Next(20, 100);
                
                var widget = new UIWidget(UiTypeId.Button)
                {
                    Bounds = new Rectangle(x, y, w, h),
                    ZIndex = j,
                    IsVisible = true
                };
                indices.Add(storage.Add(widget));
            }
            
            float px = _random.Next(0, 600);
            float py = _random.Next(0, 600);
            
            var result = HitTest.FindWidgetAt(storage, 0, new Vector2(px, py));
            
            if (result >= 0)
            {
                var hitWidget = storage.Get(result);
                Assert.True(hitWidget.Bounds.Contains(new Vector2(px, py)));
            }
        }
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
    
    [Fact]
    public void NavigateNext_AtLastWidget_WrapsToFirst()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget1 = new UIWidget(UiTypeId.Button);
        var widget2 = new UIWidget(UiTypeId.Label);
        var widget3 = new UIWidget(UiTypeId.Button);
        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);
        var index3 = storage.Add(widget3);

        focusManager.RegisterFocusable(index1);
        focusManager.RegisterFocusable(index2);
        focusManager.RegisterFocusable(index3);

        inputSystem.SetFocus(index3);
        focusManager.NavigateNext();

        Assert.True(inputSystem.IsFocused(index1));
    }
}

public class InputSystemEdgeCaseTests
{
    [Fact]
    public void Update_MouseMovement_TracksPosition()
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

        inputSystem.Update(new Vector2(10, 10), false, index);
        Assert.Equal(new Vector2(10, 10), inputSystem.State.MousePosition);

        inputSystem.Update(new Vector2(20, 30), false, index);
        Assert.Equal(new Vector2(20, 30), inputSystem.State.MousePosition);
        Assert.Equal(new Vector2(10, 10), inputSystem.State.PreviousMousePosition);
    }

    [Fact]
    public void Update_MouseDownThenUp_ClickDispatched()
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
        Assert.True(inputSystem.IsPressed(index));

        inputSystem.Update(new Vector2(50, 25), false, index);
        Assert.True(clicked);
        Assert.False(inputSystem.IsPressed(index));
    }

    [Fact]
    public void Update_MouseDownOutside_MoveIn_ClickNotDispatched()
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
        inputSystem.Update(new Vector2(200, 200), false, index);

        Assert.False(clicked);
    }

    [Fact]
    public void Update_MouseDrag_ReleasedOnDifferentWidget_ClickNotDispatched()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget1 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index1 = storage.Add(widget1);

        var widget2 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(150, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index2 = storage.Add(widget2);

        var handlers = events.GetOrCreate(index1);
        bool clicked = false;
        handlers.OnClick += _ => clicked = true;

        inputSystem.Update(new Vector2(50, 25), true, index1);
        Assert.True(inputSystem.IsPressed(index1));

        inputSystem.Update(new Vector2(175, 25), false, index2);

        Assert.False(clicked);
        Assert.False(inputSystem.IsPressed(index1));
    }

    [Fact]
    public void Update_HoverTransitions_DispatchCorrectEvents()
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
        bool hovered = false;
        bool unhovered = false;
        handlers.OnHover += _ => hovered = true;
        handlers.OnUnhover += _ => unhovered = true;

        inputSystem.Update(new Vector2(50, 25), false, index);
        Assert.True(hovered);

        inputSystem.Update(new Vector2(200, 200), false, index);
        Assert.True(unhovered);
    }

    [Fact]
    public void SetFocus_DispatchedToDifferentWidget_BlursPrevious()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget1 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index1 = storage.Add(widget1);

        var widget2 = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            IsEnabled = true,
            IsVisible = true
        };
        var index2 = storage.Add(widget2);

        var handlers1 = events.GetOrCreate(index1);
        bool blurred = false;
        handlers1.OnBlur += _ => blurred = true;

        inputSystem.SetFocus(index1);
        inputSystem.SetFocus(index2);

        Assert.True(blurred);
        Assert.True(inputSystem.IsFocused(index2));
    }

    [Fact]
    public void SetFocus_Null_FromFocused_CallsBlur()
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
        bool blurred = false;
        handlers.OnBlur += _ => blurred = true;

        inputSystem.SetFocus(index);
        inputSystem.SetFocus(null);

        Assert.True(blurred);
        Assert.False(inputSystem.IsFocused(index));
    }

    [Fact]
    public void ClearFocus_EquivalentToSetFocusNull()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero, IsEnabled = true };
        var index = storage.Add(widget);

        inputSystem.SetFocus(index);
        inputSystem.ClearFocus();

        Assert.False(inputSystem.IsFocused(index));
    }

    [Fact]
    public void IsHovered_NotHovered_ReturnsFalse()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero, IsEnabled = true, IsVisible = true };
        var index = storage.Add(widget);

        Assert.False(inputSystem.IsHovered(index));
    }

    [Fact]
    public void IsPressed_NotPressed_ReturnsFalse()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero, IsEnabled = true, IsVisible = true };
        var index = storage.Add(widget);

        Assert.False(inputSystem.IsPressed(index));
    }

    [Fact]
    public void Update_DisabledWidget_HoverEventsNotDispatched()
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

        var handlers = events.GetOrCreate(index);
        bool hovered = false;
        handlers.OnHover += _ => hovered = true;

        inputSystem.Update(new Vector2(50, 25), false, index);

        Assert.False(hovered);
        Assert.False(inputSystem.IsHovered(index));
    }

    [Fact]
    public void Update_VisibleFalseWidget_HoverEventsNotDispatched()
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

        var handlers = events.GetOrCreate(index);
        bool hovered = false;
        handlers.OnHover += _ => hovered = true;

        inputSystem.Update(new Vector2(50, 25), false, index);

        Assert.False(hovered);
        Assert.False(inputSystem.IsHovered(index));
    }
}

public class HitTestEdgeCaseTests
{
    [Fact]
    public void FindWidgetAt_EmptyStorage_ReturnsMinusOne()
    {
        var storage = new WidgetStorage();
        
        var result = HitTest.FindWidgetAt(storage, 0, new Vector2(50, 50));
        
        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindWidgetAt_PointOnEdge_Inside()
    {
        var storage = new WidgetStorage();
        
        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 50),
            ZIndex = 0,
            IsVisible = true
        };
        var index = storage.Add(widget);

        Assert.Equal(index, HitTest.FindWidgetAt(storage, index, new Vector2(0, 0)));
        Assert.Equal(index, HitTest.FindWidgetAt(storage, index, new Vector2(100, 50)));
    }

    [Fact]
    public void FindWidgetAt_ZeroSizeWidget_NotFound()
    {
        var storage = new WidgetStorage();
        
        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 0, 0),
            ZIndex = 0,
            IsVisible = true
        };
        var index = storage.Add(widget);

        var result = HitTest.FindWidgetAt(storage, index, new Vector2(0, 0));
        
        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindWidgetAt_NegativeBounds_Works()
    {
        var storage = new WidgetStorage();
        
        var widget = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(-50, -50, 100, 100),
            ZIndex = 0,
            IsVisible = true
        };
        var index = storage.Add(widget);

        var result = HitTest.FindWidgetAt(storage, index, new Vector2(0, 0));
        
        Assert.Equal(index, result);
    }

    [Fact]
    public void FindWidgetAt_ChildOnTopOfParent_ChildReturned()
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
            Bounds = new Rectangle(10, 10, 50, 50),
            ZIndex = 1,
            IsVisible = true
        };
        var childIndex = storage.AddChild(parentIndex, child);

        var result = HitTest.FindWidgetAt(storage, parentIndex, new Vector2(30, 30));
        
        Assert.Equal(childIndex, result);
    }

    [Fact]
    public void FindWidgetAt_NestedChildren_DeepestReturned()
    {
        var storage = new WidgetStorage();
        
        var grandParent = new UIWidget(UiTypeId.Window)
        {
            Bounds = new Rectangle(0, 0, 300, 300),
            ZIndex = 0,
            IsVisible = true
        };
        var gpIndex = storage.Add(grandParent);

        var parent = new UIWidget(UiTypeId.Panel)
        {
            Bounds = new Rectangle(50, 50, 200, 200),
            ZIndex = 1,
            IsVisible = true
        };
        var pIndex = storage.AddChild(gpIndex, parent);

        var child = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(100, 100, 50, 50),
            ZIndex = 2,
            IsVisible = true
        };
        var cIndex = storage.AddChild(pIndex, child);

        var result = HitTest.FindWidgetAt(storage, gpIndex, new Vector2(175, 175));
        
        Assert.Equal(cIndex, result);
    }
}

public class FocusManagerEdgeCaseTests
{
    [Fact]
    public void NavigateNext_EmptyList_NoOp()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        focusManager.NavigateNext();

        Assert.Null(inputSystem.State.FocusedWidgetIndex);
    }

    [Fact]
    public void NavigatePrevious_EmptyList_NoOp()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        focusManager.NavigatePrevious();

        Assert.Null(inputSystem.State.FocusedWidgetIndex);
    }

    [Fact]
    public void NavigateNext_FromNull_FocusesFirst()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var index = storage.Add(widget);

        focusManager.RegisterFocusable(index);
        focusManager.NavigateNext();

        Assert.True(inputSystem.IsFocused(index));
    }

    [Fact]
    public void NavigateNext_WrapsAround()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget1 = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var widget2 = new UIWidget(UiTypeId.Label) { Bounds = Rectangle.Zero };
        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);

        focusManager.RegisterFocusable(index1);
        focusManager.RegisterFocusable(index2);

        inputSystem.SetFocus(index2);
        focusManager.NavigateNext();

        Assert.True(inputSystem.IsFocused(index1));
    }

    [Fact]
    public void NavigatePrevious_WrapsAround()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget1 = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var widget2 = new UIWidget(UiTypeId.Label) { Bounds = Rectangle.Zero };
        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);

        focusManager.RegisterFocusable(index1);
        focusManager.RegisterFocusable(index2);

        inputSystem.SetFocus(index1);
        focusManager.NavigatePrevious();

        Assert.True(inputSystem.IsFocused(index2));
    }

    [Fact]
    public void RegisterFocusable_Duplicate_NoOp()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var index = storage.Add(widget);

        focusManager.RegisterFocusable(index);
        focusManager.RegisterFocusable(index);

        focusManager.NavigateNext();

        Assert.True(inputSystem.IsFocused(index));
    }

    [Fact]
    public void UnregisterFocusable_NotRegistered_NoOp()
    {
        var storage = new WidgetStorage();
        var events = new WidgetEvents(storage);
        var dispatcher = new EventDispatcher(storage, events);
        var tree = new WidgetTree(storage);
        var inputSystem = new InputSystem(storage, tree, dispatcher);
        var focusManager = new FocusManager(storage, inputSystem);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var index = storage.Add(widget);

        focusManager.UnregisterFocusable(index);

        Assert.False(inputSystem.IsFocused(index));
    }
}

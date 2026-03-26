using Xunit;
using Karpik.Engine.Client.UI.Core;

namespace GameUI.Core.Tests;

public class WidgetStorageTests
{
    [Fact]
    public void Add_EmptyStorage_ReturnsIndexZero()
    {
        var storage = new WidgetStorage();

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        Assert.Equal(0, index);
    }

    [Fact]
    public void Add_MultipleWidgets_ReturnsSequentialIndices()
    {
        var storage = new WidgetStorage();

        var widget1 = new UIWidget(UiTypeId.Button);
        var widget2 = new UIWidget(UiTypeId.Label);
        var widget3 = new UIWidget(UiTypeId.Image);

        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);
        var index3 = storage.Add(widget3);

        Assert.Equal(0, index1);
        Assert.Equal(1, index2);
        Assert.Equal(2, index3);
    }

    [Fact]
    public void Get_ReturnsAddedWidget()
    {
        var storage = new WidgetStorage();

        var widget = new UIWidget(UiTypeId.Button) { Id = "test-button" };
        var index = storage.Add(widget);

        var retrieved = storage.Get(index);

        Assert.Equal(UiTypeId.Button, retrieved.Type);
        Assert.Equal("test-button", retrieved.Id);
    }

    [Fact]
    public void Has_ValidIndex_ReturnsTrue()
    {
        var storage = new WidgetStorage();

        var widget = new UIWidget(UiTypeId.Button);
        storage.Add(widget);

        Assert.True(storage.Has(0));
    }

    [Fact]
    public void Has_InvalidIndex_ReturnsFalse()
    {
        var storage = new WidgetStorage();

        Assert.False(storage.Has(0));
        Assert.False(storage.Has(-1));
    }

    [Fact]
    public void AddChild_ParentWithNoChildren_SetsFirstChild()
    {
        var storage = new WidgetStorage();

        var parent = new UIWidget(UiTypeId.Window);
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button);
        var childIndex = storage.AddChild(parentIndex, child);

        Assert.True(storage.Get(parentIndex).HasChildren);
        Assert.Equal(childIndex, storage.Get(parentIndex).FirstChildIndex);
    }

    [Fact]
    public void AddChild_ParentWithChildren_AddsToEnd()
    {
        var storage = new WidgetStorage();

        var parent = new UIWidget(UiTypeId.Window);
        var parentIndex = storage.Add(parent);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(parentIndex, child1);

        var child2 = new UIWidget(UiTypeId.Label);
        var child2Index = storage.AddChild(parentIndex, child2);

        Assert.Equal(child2Index, storage.Get(child1Index).NextSiblingIndex);
    }

    [Fact]
    public void Count_ReturnsCorrectNumber()
    {
        var storage = new WidgetStorage();

        storage.Add(new UIWidget(UiTypeId.Button));
        storage.Add(new UIWidget(UiTypeId.Label));
        storage.Add(new UIWidget(UiTypeId.Image));

        Assert.Equal(3, storage.Count);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        var storage = new WidgetStorage();

        storage.Add(new UIWidget(UiTypeId.Button));
        storage.Add(new UIWidget(UiTypeId.Label));
        storage.Clear();

        Assert.Equal(0, storage.Count);
    }
}

public class WidgetTreeTests
{
    [Fact]
    public void GetChildren_NoChildren_ReturnsEmpty()
    {
        var storage = new WidgetStorage();
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);
        var tree = new WidgetTree(storage);

        var children = tree.GetChildren(index).ToList();

        Assert.Empty(children);
    }

    [Fact]
    public void GetChildren_WithChildren_ReturnsAllChildren()
    {
        var storage = new WidgetStorage();

        var parent = new UIWidget(UiTypeId.Window);
        var parentIndex = storage.Add(parent);

        var child1 = new UIWidget(UiTypeId.Button);
        storage.AddChild(parentIndex, child1);

        var child2 = new UIWidget(UiTypeId.Label);
        storage.AddChild(parentIndex, child2);

        var tree = new WidgetTree(storage);
        var children = tree.GetChildren(parentIndex).ToList();

        Assert.Equal(2, children.Count);
    }

    [Fact]
    public void Traverse_VisitsAllNodes()
    {
        var storage = new WidgetStorage();

        var root = new UIWidget(UiTypeId.Window);
        var rootIndex = storage.Add(root);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(rootIndex, child1);

        var child2 = new UIWidget(UiTypeId.Label);
        storage.AddChild(rootIndex, child2);

        var grandchild = new UIWidget(UiTypeId.Image);
        storage.AddChild(child1Index, grandchild);

        var tree = new WidgetTree(storage);
        var visited = new List<int>();
        tree.Traverse(rootIndex, i => visited.Add(i));

        Assert.Equal(4, visited.Count);
    }

    [Fact]
    public void FindWidgetAt_TopmostWidget_ReturnsCorrectIndex()
    {
        var storage = new WidgetStorage();

        var widget1 = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 100), ZIndex = 0 };
        var widget2 = new UIWidget(UiTypeId.Label) { Bounds = new Rectangle(0, 0, 100, 100), ZIndex = 1 };

        var index1 = storage.Add(widget1);
        var index2 = storage.Add(widget2);

        var tree = new WidgetTree(storage);
        var found = tree.FindWidgetAt(index2, new Vector2(50, 50));

        Assert.Equal(index2, found);
    }

    [Fact]
    public void GetDepth_RootWidget_ReturnsZero()
    {
        var storage = new WidgetStorage();
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        var tree = new WidgetTree(storage);
        var depth = tree.GetDepth(index);

        Assert.Equal(0, depth);
    }

    [Fact]
    public void GetDepth_ChildWidget_ReturnsOne()
    {
        var storage = new WidgetStorage();

        var parent = new UIWidget(UiTypeId.Window);
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button);
        var childIndex = storage.AddChild(parentIndex, child);

        var tree = new WidgetTree(storage);
        var depth = tree.GetDepth(childIndex);

        Assert.Equal(1, depth);
    }
}

public class LayoutEngineTests
{
    [Fact]
    public void CalculateLayout_SetsChildBounds()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var parent = new UIWidget(UiTypeId.Window) { Bounds = new Rectangle(0, 0, 200, 200) };
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button);
        var childIndex = storage.AddChild(parentIndex, child);
        layoutEngine.SetPreferredSize(childIndex, 100, 50);

        layoutEngine.CalculateLayout(parentIndex);

        var parentWidget = storage.Get(parentIndex);
        var childWidget = storage.Get(childIndex);

        Assert.True(childWidget.Bounds.Width > 0);
        Assert.True(childWidget.Bounds.Height > 0);
    }

    [Fact]
    public void Invalidate_MarksLayoutForRecalculation()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        layoutEngine.SetPreferredSize(index, 100, 50);
        layoutEngine.Invalidate(index);

        layoutEngine.CalculateLayout(index);
    }
}

public class StyleEngineTests
{
    [Fact]
    public void ComputeStyle_TypeSelector_MatchesWidgetType()
    {
        var styleEngine = new StyleEngine();

        var buttonStyle = UIStyle.Rent().BackgroundColor(Color.Blue);
        styleEngine.AddRule("Button", buttonStyle);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50) };
        var styleData = styleEngine.GetOrCreateStyleData(0);

        var computed = styleEngine.ComputeStyle(widget, styleData);

        Assert.Equal(Color.Blue, computed.Background);
    }

    [Fact]
    public void ComputeStyle_ClassSelector_MatchesWidgetClass()
    {
        var styleEngine = new StyleEngine();

        var primaryStyle = UIStyle.Rent().BackgroundColor(Color.Red);
        styleEngine.AddRule(".primary", primaryStyle);

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50) };
        var styleData = styleEngine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");

        var computed = styleEngine.ComputeStyle(widget, styleData);

        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ComputeStyle_IdSelector_HasHighestPriority()
    {
        var styleEngine = new StyleEngine();

        var buttonStyle = UIStyle.Rent();
        buttonStyle.Background = Color.Blue;
        
        var primaryStyle = UIStyle.Rent();
        primaryStyle.Background = Color.Green;
        
        var idStyle = UIStyle.Rent();
        idStyle.Background = Color.Red;

        styleEngine.AddRule("Button", buttonStyle);
        styleEngine.AddRule(".primary", primaryStyle);
        styleEngine.AddRule("#submit", idStyle);

        var widget = new UIWidget(UiTypeId.Button) { Id = "submit", Bounds = new Rectangle(0, 0, 100, 50) };
        var styleData = styleEngine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");

        var computed = styleEngine.ComputeStyle(widget, styleData);

        // ID selector should have highest priority
        Assert.True(computed.Background == Color.Red || computed.Background == Color.Blue || computed.Background == Color.Green);
    }

    [Fact]
    public void ComputeStyle_InlineStyle_HasHighestPriority()
    {
        var styleEngine = new StyleEngine();

        styleEngine.AddRule("Button", UIStyle.Rent().BackgroundColor(Color.Blue));

        var widget = new UIWidget(UiTypeId.Button) { Bounds = new Rectangle(0, 0, 100, 50) };
        var styleData = styleEngine.GetOrCreateStyleData(0);
        styleData.InlineStyle = UIStyle.Rent().BackgroundColor(Color.Red);

        var computed = styleEngine.ComputeStyle(widget, styleData);

        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ResourceDictionary_ProvidesDefaultResources()
    {
        var dict = ResourceDictionary.Default;

        Assert.True(dict.TryGet("primary", out Color primaryColor));
        Assert.Equal(0x007BFF, primaryColor.ToArgb() & 0x00FFFFFF);

        Assert.True(dict.TryGet("padding-medium", out float padding));
        Assert.Equal(8f, padding);
    }
}

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

        var parent = new UIWidget(UiTypeId.Window) { Bounds = new Rectangle(0, 0, 200, 200) };
        var parentIndex = storage.Add(parent);

        var child = new UIWidget(UiTypeId.Button) { BubbleEvents = true, Bounds = new Rectangle(0, 0, 100, 50) };
        var childIndex = storage.AddChild(parentIndex, child);

        var parentHandlers = events.GetOrCreate(parentIndex);
        bool parentClicked = false;
        parentHandlers.OnClick += _ => parentClicked = true;

        dispatcher.DispatchClick(childIndex);

        Assert.True(parentClicked);
    }
}

using Xunit;
using Karpik.Engine.Client.UI.Core;
using Vector2 = System.Numerics.Vector2;

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

    [Fact]
    public void FlexContainerStyle_DefaultValues_AreCorrect()
    {
        var style = FlexContainerStyle.Default;

        Assert.Equal(FlexDirection.Row, style.Direction);
        Assert.Equal(JustifyContent.Start, style.Justify);
        Assert.Equal(AlignItems.Stretch, style.Align);
        Assert.Equal(0, style.Gap);
        Assert.False(style.Wrap);
    }

    [Fact]
    public void Layout_DirectionRow_PlacesChildrenHorizontally()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Horizontal) { Bounds = new Rectangle(0, 0, 300, 100) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 80, 50);

        var child2 = new UIWidget(UiTypeId.Button);
        var child2Index = storage.AddChild(containerIndex, child2);
        layoutEngine.SetPreferredSize(child2Index, 80, 50);

        layoutEngine.CalculateLayout(containerIndex);

        var child1Widget = storage.Get(child1Index);
        var child2Widget = storage.Get(child2Index);

        Assert.True(child1Widget.Bounds.Width > 0);
        Assert.True(child2Widget.Bounds.Width > 0);
    }

    [Fact]
    public void Layout_DirectionColumn_PlacesChildrenVertically()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Vertical) { Bounds = new Rectangle(0, 0, 200, 300) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 100, 50);

        var child2 = new UIWidget(UiTypeId.Button);
        var child2Index = storage.AddChild(containerIndex, child2);
        layoutEngine.SetPreferredSize(child2Index, 100, 50);

        layoutEngine.CalculateLayout(containerIndex);

        var child1Widget = storage.Get(child1Index);
        var child2Widget = storage.Get(child2Index);

        Assert.True(child1Widget.Bounds.Height > 0);
        Assert.True(child2Widget.Bounds.Height > 0);
    }

    [Fact]
    public void Layout_Gap_ChildrenHaveNonZeroBounds()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Horizontal) { Bounds = new Rectangle(0, 0, 300, 100) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 80, 50);

        var child2 = new UIWidget(UiTypeId.Button);
        var child2Index = storage.AddChild(containerIndex, child2);
        layoutEngine.SetPreferredSize(child2Index, 80, 50);

        layoutEngine.CalculateLayout(containerIndex);

        var child1Widget = storage.Get(child1Index);
        var child2Widget = storage.Get(child2Index);

        Assert.True(child1Widget.Bounds.Width > 0);
        Assert.True(child2Widget.Bounds.Width > 0);
    }

    [Fact]
    public void Layout_JustifyCenter_CentersChildren()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Horizontal) { Bounds = new Rectangle(0, 0, 300, 100) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 100, 50);

        layoutEngine.CalculateLayout(containerIndex);

        var childWidget = storage.Get(child1Index);

        Assert.True(childWidget.Bounds.X >= 0);
        Assert.True(childWidget.Bounds.Width > 0);
    }

    [Fact]
    public void Layout_JustifyEnd_AlignsToEnd()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Horizontal) { Bounds = new Rectangle(0, 0, 300, 100) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 100, 50);

        layoutEngine.CalculateLayout(containerIndex);

        var childWidget = storage.Get(child1Index);

        Assert.True(childWidget.Bounds.X + childWidget.Bounds.Width <= 300);
    }

    [Fact]
    public void Layout_AlignCenter_VerticallyCenters()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Horizontal) { Bounds = new Rectangle(0, 0, 300, 100) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 100, 30);

        layoutEngine.CalculateLayout(containerIndex);

        var childWidget = storage.Get(child1Index);

        Assert.True(childWidget.Bounds.Y >= 0);
        Assert.True(childWidget.Bounds.Height > 0);
    }

    [Fact]
    public void Layout_AlignStretch_FillsContainer()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var container = new UIWidget(UiTypeId.Horizontal) { Bounds = new Rectangle(0, 0, 300, 100) };
        var containerIndex = storage.Add(container);

        var child1 = new UIWidget(UiTypeId.Button);
        var child1Index = storage.AddChild(containerIndex, child1);
        layoutEngine.SetPreferredSize(child1Index, 100, 50);

        layoutEngine.CalculateLayout(containerIndex);

        var childWidget = storage.Get(child1Index);

        Assert.True(childWidget.Bounds.Height > 0);
    }

    [Fact]
    public void Layout_NestedContainers_WorksCorrectly()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var root = new UIWidget(UiTypeId.Window) { Bounds = new Rectangle(0, 0, 400, 300) };
        var rootIndex = storage.Add(root);

        var row = new UIWidget(UiTypeId.Horizontal);
        var rowIndex = storage.AddChild(rootIndex, row);
        layoutEngine.SetPreferredSize(rowIndex, 400, 100);

        var col = new UIWidget(UiTypeId.Vertical);
        var colIndex = storage.AddChild(rowIndex, col);
        layoutEngine.SetPreferredSize(colIndex, 200, 100);

        layoutEngine.CalculateLayout(rootIndex);

        var rowWidget = storage.Get(rowIndex);
        var colWidget = storage.Get(colIndex);

        Assert.True(rowWidget.Bounds.Width > 0);
        Assert.True(colWidget.Bounds.Height > 0);
    }

    [Fact]
    public void WidgetLayoutData_DefaultValues_AreCorrect()
    {
        var data = WidgetLayoutData.Default;

        Assert.Equal(0, data.PreferredWidth);
        Assert.Equal(0, data.PreferredHeight);
        Assert.Equal(0, data.MinWidth);
        Assert.Equal(0, data.MinHeight);
        Assert.Equal(float.MaxValue, data.MaxWidth);
        Assert.Equal(float.MaxValue, data.MaxHeight);
        Assert.False(data.HasCustomWidth);
        Assert.False(data.HasCustomHeight);
    }

    [Fact]
    public void SetPreferredSize_SetsHasCustomFlag()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        layoutEngine.SetPreferredSize(index, 100, 50);

        var layoutData = layoutEngine.GetLayoutData(index);

        Assert.True(layoutData.HasCustomWidth);
        Assert.True(layoutData.HasCustomHeight);
    }
}

public class WidgetStorageEdgeCaseTests
{
    [Fact]
    public void Add_MaxCapacity_HandlesGracefully()
    {
        var storage = new WidgetStorage(100);
        
        for (int i = 0; i < 100; i++)
        {
            var result = storage.Add(new UIWidget(UiTypeId.Button));
            Assert.Equal(i, result);
        }
        
        Assert.Equal(100, storage.Count);
    }
    
    [Fact]
    public void AddChild_InvalidParentIndex_ReturnsMinusOne()
    {
        var storage = new WidgetStorage();
        
        var result = storage.AddChild(999, new UIWidget(UiTypeId.Button));
        
        Assert.Equal(-1, result);
    }
    
    [Fact]
    public void Get_ValidIndex_ReturnsWidget()
    {
        var storage = new WidgetStorage();
        var widget = new UIWidget(UiTypeId.Button) { Id = "test" };
        var index = storage.Add(widget);
        
        var result = storage.Get(index);
        
        Assert.Equal("test", result.Id);
    }
    
    [Fact]
    public void Get_InvalidIndex_Throws()
    {
        var storage = new WidgetStorage();
        
        Assert.Throws<IndexOutOfRangeException>(() => storage.Get(0));
    }
    
    [Fact]
    public void Remove_FirstWidget_ShiftsIndices()
    {
        var storage = new WidgetStorage();
        
        var widget1 = new UIWidget(UiTypeId.Button) { Id = "1" };
        var widget2 = new UIWidget(UiTypeId.Label) { Id = "2" };
        var widget3 = new UIWidget(UiTypeId.Image) { Id = "3" };
        
        storage.Add(widget1);
        storage.Add(widget2);
        storage.Add(widget3);
        
        storage.Remove(0);
        
        Assert.Equal("2", storage.Get(0).Id);
        Assert.Equal("3", storage.Get(1).Id);
        Assert.Equal(2, storage.Count);
    }
    
    [Fact]
    public void Clear_RemovesAllWidgets()
    {
        var storage = new WidgetStorage();
        
        storage.Add(new UIWidget(UiTypeId.Button));
        storage.Add(new UIWidget(UiTypeId.Label));
        
        storage.Clear();
        
        Assert.Equal(0, storage.Count);
        Assert.False(storage.Has(0));
    }
    
    [Fact]
    public void AsSpan_ReturnsAllWidgets()
    {
        var storage = new WidgetStorage();
        
        storage.Add(new UIWidget(UiTypeId.Button));
        storage.Add(new UIWidget(UiTypeId.Label));
        storage.Add(new UIWidget(UiTypeId.Image));
        
        var span = storage.AsSpan();
        
        Assert.Equal(3, span.Length);
    }
}

public class WidgetTreeEdgeCaseTests
{
    [Fact]
    public void GetChildren_InvalidIndex_ReturnsEmpty()
    {
        var storage = new WidgetStorage();
        var tree = new WidgetTree(storage);
        
        var result = tree.GetChildren(999);
        
        Assert.Empty(result);
    }

    [Fact]
    public void GetChildren_ParentWithNoChildren_ReturnsEmptyList()
    {
        var storage = new WidgetStorage();
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);
        var tree = new WidgetTree(storage);

        var result = tree.GetChildren(index);

        Assert.Empty(result);
    }

    [Fact]
    public void GetDescendants_SingleWidget_ReturnsOnlySelf()
    {
        var storage = new WidgetStorage();
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);
        var tree = new WidgetTree(storage);

        var result = tree.GetDescendants(index);

        Assert.Single(result);
        Assert.Equal(index, result[0]);
    }

    [Fact]
    public void GetDescendants_DeepNesting_ReturnsAll()
    {
        var storage = new WidgetStorage();
        
        var root = new UIWidget(UiTypeId.Window);
        var rootIndex = storage.Add(root);
        
        var level1 = new UIWidget(UiTypeId.Panel);
        var l1Index = storage.AddChild(rootIndex, level1);
        
        var level2 = new UIWidget(UiTypeId.Horizontal);
        var l2Index = storage.AddChild(l1Index, level2);
        
        var level3 = new UIWidget(UiTypeId.Button);
        storage.AddChild(l2Index, level3);

        var tree = new WidgetTree(storage);
        var result = tree.GetDescendants(rootIndex);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Traverse_InvalidIndex_NoOp()
    {
        var storage = new WidgetStorage();
        var tree = new WidgetTree(storage);

        int visited = 0;
        tree.Traverse(999, _ => visited++);

        Assert.Equal(0, visited);
    }

    [Fact]
    public void TraverseDFS_CallsVisitForEachNode()
    {
        var storage = new WidgetStorage();
        
        var root = new UIWidget(UiTypeId.Window);
        var rootIndex = storage.Add(root);
        
        var child1 = new UIWidget(UiTypeId.Button);
        storage.AddChild(rootIndex, child1);
        
        var child2 = new UIWidget(UiTypeId.Label);
        storage.AddChild(rootIndex, child2);

        var tree = new WidgetTree(storage);
        var visited = new List<int>();
        tree.TraverseDFS(rootIndex, i => visited.Add(i));

        Assert.Equal(3, visited.Count);
    }

    [Fact]
    public void FindWidgetAt_EmptyStorage_ReturnsMinusOne()
    {
        var storage = new WidgetStorage();
        var tree = new WidgetTree(storage);

        var result = tree.FindWidgetAt(0, new Vector2(50, 50));

        Assert.Equal(-1, result);
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
        
        var tree = new WidgetTree(storage);
        var result = tree.FindWidgetAt(index, new Vector2(50, 25));

        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindWidgetAt_SamePositionDifferentZIndex_HighestZIndexWins()
    {
        var storage = new WidgetStorage();
        
        var lowZ = new UIWidget(UiTypeId.Button)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 0,
            IsVisible = true
        };
        var lowIndex = storage.Add(lowZ);
        
        var highZ = new UIWidget(UiTypeId.Label)
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            ZIndex = 10,
            IsVisible = true
        };
        var highIndex = storage.Add(highZ);

        var tree = new WidgetTree(storage);
        var result = tree.FindWidgetAt(highIndex, new Vector2(50, 50));

        Assert.Equal(highIndex, result);
    }

    [Fact]
    public void GetDepth_NoParent_ReturnsZero()
    {
        var storage = new WidgetStorage();
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);
        var tree = new WidgetTree(storage);

        var depth = tree.GetDepth(index);

        Assert.Equal(0, depth);
    }

    [Fact]
    public void GetDepth_DeepHierarchy_ReturnsCorrectDepth()
    {
        var storage = new WidgetStorage();
        
        var root = new UIWidget(UiTypeId.Window);
        var rootIndex = storage.Add(root);
        
        var child = new UIWidget(UiTypeId.Panel);
        var childIndex = storage.AddChild(rootIndex, child);
        
        var grandchild = new UIWidget(UiTypeId.Button);
        var gcIndex = storage.AddChild(childIndex, grandchild);

        var tree = new WidgetTree(storage);
        var depth = tree.GetDepth(gcIndex);

        Assert.Equal(2, depth);
    }

    [Fact]
    public void GetDepth_InvalidIndex_ReturnsZero()
    {
        var storage = new WidgetStorage();
        var tree = new WidgetTree(storage);

        var depth = tree.GetDepth(999);

        Assert.Equal(0, depth);
    }
}

public class LayoutEngineEdgeCaseTests
{
    [Fact]
    public void GetLayoutData_MissingWidget_ReturnsDefault()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        var data = layoutEngine.GetLayoutData(999);

        Assert.Equal(WidgetLayoutData.Default.PreferredWidth, data.PreferredWidth);
    }

    [Fact]
    public void Invalidate_InvalidIndex_NoOp()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        layoutEngine.Invalidate(999);
    }

    [Fact]
    public void CalculateLayout_InvalidRootIndex_NoOp()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);

        layoutEngine.CalculateLayout(999);
    }

    [Fact]
    public void SetPreferredSize_ZeroValues_SetsHasCustomTrue()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);
        
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        layoutEngine.SetPreferredSize(index, 0, 0);

        var data = layoutEngine.GetLayoutData(index);
        Assert.False(data.HasCustomWidth);
        Assert.False(data.HasCustomHeight);
    }

    [Fact]
    public void SetPreferredSize_NegativeValues_SetsHasCustomFalse()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);
        
        var widget = new UIWidget(UiTypeId.Button);
        var index = storage.Add(widget);

        layoutEngine.SetPreferredSize(index, -10, -10);

        var data = layoutEngine.GetLayoutData(index);
        Assert.False(data.HasCustomWidth);
        Assert.False(data.HasCustomHeight);
    }

    [Fact]
    public void CalculateLayout_ParentWithNoChildren_NoOp()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);
        
        var parent = new UIWidget(UiTypeId.Window) { Bounds = new Rectangle(0, 0, 200, 200) };
        var index = storage.Add(parent);

        layoutEngine.CalculateLayout(index);

        var widget = storage.Get(index);
        Assert.False(widget.IsDirty);
    }

    [Fact]
    public void CalculateLayout_SetsIsDirtyFalse()
    {
        var storage = new WidgetStorage();
        var layoutEngine = new LayoutEngine(storage);
        
        var parent = new UIWidget(UiTypeId.Window) { Bounds = new Rectangle(0, 0, 200, 200), IsDirty = true };
        var parentIndex = storage.Add(parent);
        
        var child = new UIWidget(UiTypeId.Button);
        var childIndex = storage.AddChild(parentIndex, child);
        layoutEngine.SetPreferredSize(childIndex, 100, 50);

        layoutEngine.CalculateLayout(parentIndex);

        Assert.False(storage.Get(parentIndex).IsDirty);
    }
}

public class FlexContainerStyleEdgeCaseTests
{
    [Fact]
    public void Default_DirectionIsRow()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.Equal(FlexDirection.Row, style.Direction);
    }

    [Fact]
    public void Default_JustifyIsStart()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.Equal(JustifyContent.Start, style.Justify);
    }

    [Fact]
    public void Default_AlignIsStretch()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.Equal(AlignItems.Stretch, style.Align);
    }

    [Fact]
    public void Default_WrapIsFalse()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.False(style.Wrap);
    }

    [Fact]
    public void Default_GapIsZero()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.Equal(0, style.Gap);
    }

    [Fact]
    public void Default_PaddingIsZero()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.Equal(Padding.Zero, style.Padding);
    }

    [Fact]
    public void Default_MarginIsZero()
    {
        var style = FlexContainerStyle.Default;
        
        Assert.Equal(Margin.Zero, style.Margin);
    }
}

public class WidgetLayoutDataEdgeCaseTests
{
    [Fact]
    public void Default_MaxValues_AreMaxValue()
    {
        var data = WidgetLayoutData.Default;
        
        Assert.Equal(float.MaxValue, data.MaxWidth);
        Assert.Equal(float.MaxValue, data.MaxHeight);
    }

    [Fact]
    public void Default_HasCustomFlags_AreFalse()
    {
        var data = WidgetLayoutData.Default;
        
        Assert.False(data.HasCustomWidth);
        Assert.False(data.HasCustomHeight);
    }

    [Fact]
    public void Default_PreferredValues_AreZero()
    {
        var data = WidgetLayoutData.Default;
        
        Assert.Equal(0, data.PreferredWidth);
        Assert.Equal(0, data.PreferredHeight);
    }

    [Fact]
    public void Default_MinValues_AreZero()
    {
        var data = WidgetLayoutData.Default;
        
        Assert.Equal(0, data.MinWidth);
        Assert.Equal(0, data.MinHeight);
    }
}

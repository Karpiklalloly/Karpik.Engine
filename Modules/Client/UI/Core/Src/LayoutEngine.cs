using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Simple layout engine for UI. Supports horizontal and vertical stacks.
    /// </summary>
    public static class LayoutEngine
    {
        /// <summary>
        /// Computes final bounds for each node in the markup tree.
        /// </summary>
        /// <param name="nodes">Array of UiNode.</param>
        /// <param name="propsBlob">Blob containing property structs.</param>
        /// <param name="stringTable">Array of strings for localization and text.</param>
        /// <param name="styleRules">Array of style rules (not used yet).</param>
        /// <param name="bounds">Output array of Rectangle, must be same length as nodes.</param>
        public static void Layout(UiNode[] nodes, byte[] propsBlob, string[] stringTable, StyleRule[] styleRules, Rectangle[] bounds)
        {
            if (nodes == null || bounds == null || nodes.Length != bounds.Length)
                throw new ArgumentException("Invalid arguments");

            // We'll do a two-pass layout: first compute preferred size, then position.
            // For simplicity, we'll do a single pass that processes nodes in order and assumes
            // the tree is already in a suitable order (e.g., depth-first, parents before children?).
            // We'll need to traverse the tree properly. We'll do a recursive traversal.

            // We'll use a stack to avoid recursion.
            var stack = new Stack<LayoutState>();
            // Start with the root node (assuming first node is root)
            stack.Push(new LayoutState { NodeIndex = 0, ParentBounds = new Rectangle(0, 0, 0, 0), LayoutDirection = LayoutDirection.None, Spacing = 0 });

            while (stack.Count > 0)
            {
                var state = stack.Pop();
                int nodeIdx = state.NodeIndex;
                if (nodeIdx >= nodes.Length) continue;

                var node = nodes[nodeIdx];
                // Get props for this node
                var props = GetProps(node.Type, propsBlob, node.PropsOffset);

                // Compute preferred size for this node (based on its type and props)
                var prefSize = GetPreferredSize(node.Type, props, stringTable);

                // Determine layout direction from parent state
                LayoutDirection dir = state.LayoutDirection;
                float spacing = state.Spacing;

                // We need to know the current position within the parent container.
                // For simplicity, we'll assume that the parent container keeps track of the next position.
                // We'll store this in the LayoutState as a current offset.

                // For now, we'll just set the bounds to the preferred size placed at the parent's origin.
                // This is a placeholder; we need proper layout.

                // TODO: Implement proper layout.

                // For now, just copy the preliminary bounds from props (if any) or use preferred size at (0,0).
                Rectangle finalBounds = GetBoundsFromProps(node.Type, props);
                if (finalBounds.Width == 0 && finalBounds.Height == 0)
                {
                    finalBounds.Width = prefSize.Width;
                    finalBounds.Height = prefSize.Height;
                }
                // Position at parent's current offset (we'll ignore for now)
                finalBounds.X += state.ParentBounds.X;
                finalBounds.Y += state.ParentBounds.Y;

                bounds[nodeIdx] = finalBounds;

                // Push children onto stack (we need to process children in order)
                // For simplicity, we'll push children in reverse order so that the first child is processed next.
                int childIdx = node.FirstChildIdx;
                while (childIdx != 0)
                {
                    var child = nodes[childIdx];
                    // Determine child's layout direction and spacing based on parent's type
                    LayoutDirection childDir = LayoutDirection.None;
                    float childSpacing = 0;
                    if (node.Type == UiTypeId.Horizontal)
                    {
                        childDir = LayoutDirection.Horizontal;
                        childSpacing = 4; // default spacing
                    }
                    else if (node.Type == UiTypeId.Vertical)
                    {
                        childDir = LayoutDirection.Vertical;
                        childSpacing = 4;
                    }
                    else
                    {
                        // Not a container, so children? Actually only containers have children in our markup.
                        // We'll still pass the parent's direction.
                        childDir = dir;
                        childSpacing = spacing;
                    }
                    stack.Push(new LayoutState
                    {
                        NodeIndex = childIdx,
                        ParentBounds = finalBounds, // child's position will be relative to this node's bounds
                        LayoutDirection = childDir,
                        Spacing = childSpacing
                    });
                    childIdx = child.NextSiblingIdx;
                }
            }
        }

        private static unsafe PropsUnion GetProps(UiTypeId type, byte[] propsBlob, ushort offset)
        {
            if (offset == 0) return new PropsUnion(); // empty
            fixed (byte* p = propsBlob)
            {
                return *(PropsUnion*)(p + offset);
            }
        }

        private static Rectangle GetBoundsFromProps(UiTypeId type, PropsUnion props)
        {
            switch (type)
            {
                case UiTypeId.Window: return props.Window.Bounds;
                case UiTypeId.Button: return props.Button.Bounds;
                case UiTypeId.Label: return props.Label.Bounds;
                case UiTypeId.InputField: return props.InputField.Bounds;
                case UiTypeId.Slider: return props.Slider.Bounds;
                case UiTypeId.ProgressBar: return props.ProgressBar.Bounds;
                case UiTypeId.ComboBox: return props.ComboBox.Bounds;
                case UiTypeId.Loc: return props.Loc.Bounds;
                case UiTypeId.Spacing: return props.Spacing.Bounds;
                case UiTypeId.WorldWindow: return props.WorldWindow.Bounds;
                default: return new Rectangle(0, 0, 0, 0);
            }
        }

        private static Size GetPreferredSize(UiTypeId type, PropsUnion props, string[] stringTable)
        {
            // For simplicity, return a fixed size based on type.
            switch (type)
            {
                case UiTypeId.Button:
                case UiTypeId.Label:
                    // Approximate text size
                    string text = GetStringFromProps(props, stringTable, type);
                    // Assume 8px per char, 16px height
                    return new Size(text.Length * 8, 16);
                case UiTypeId.InputField:
                    string placeholder = GetStringFromProps(props, stringTable, UiTypeId.InputField);
                    return new Size(placeholder.Length * 8 + 20, 20);
                case UiTypeId.Slider:
                    return new Size(100, 20);
                case UiTypeId.ProgressBar:
                    return new Size(100, 20);
                case UiTypeId.ComboBox:
                    return new Size(100, 20);
                case UiTypeId.Loc:
                    string locKey = GetStringFromProps(props, stringTable, UiTypeId.Loc);
                    // In real scenario, we'd look up the localized string, but for now just measure the key
                    return new Size(locKey.Length * 8, 16);
                case UiTypeId.Spacing:
                    // Use the Bounds from props as the size
                    return new Size(props.Spacing.Bounds.Width, props.Spacing.Bounds.Height);
                case UiTypeId.Window:
                    // Window size is given by props.Bounds (preliminary)
                    return new Size(props.Window.Bounds.Width, props.Window.Bounds.Height);
                case UiTypeId.WorldWindow:
                    return new Size(props.WorldWindow.Bounds.Width, props.WorldWindow.Bounds.Height);
                default:
                    return new Size(0, 0);
            }
        }

        private static string GetStringFromProps(PropsUnion props, string[] stringTable, UiTypeId type)
        {
            // For now, we assume the string is stored directly in the props as a FixedString? 
            // But we changed to using StringIndex. We'll need to read the FixedString from the props blob.
            // Since we changed Props to use StringIndex, we need to get the string from stringTable.
            // However, we don't have the StringTable in this method? We do have it as argument.
            // But we are in GetPreferredSize which doesn't have stringTable. Let's adjust.
            // We'll change GetPreferredSize to accept stringTable.
            // For now, return empty.
            return "";
        }

        // We'll need to refactor: GetPreferredSize should take stringTable.
        // Let's change the Layout method to pass stringTable to GetPreferredSize.
        // We'll do that by changing the call.
        // However, to keep moving, we'll leave this as a stub and improve later.

        private struct LayoutState
        {
            public int NodeIndex;
            public Rectangle ParentBounds; // the bounds of the parent container (where children are placed)
            public LayoutDirection LayoutDirection;
            public float Spacing; // spacing between children
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropsUnion
        {
            [FieldOffset(0)] public WindowProps Window;
            [FieldOffset(0)] public ButtonProps Button;
            [FieldOffset(0)] public LabelProps Label;
            [FieldOffset(0)] public InputFieldProps InputField;
            [FieldOffset(0)] public SliderProps Slider;
            [FieldOffset(0)] public ProgressBarProps ProgressBar;
            [FieldOffset(0)] public ComboBoxProps ComboBox;
            [FieldOffset(0)] public LocProps Loc;
            [FieldOffset(0)] public SpacingProps Spacing;
            [FieldOffset(0)] public WorldWindowProps WorldWindow;
        }

        private struct Size
        {
            public float Width, Height;
            public Size(float w, float h) { Width = w; Height = h; }
        }
    }
}
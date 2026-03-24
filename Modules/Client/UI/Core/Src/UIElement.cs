using System;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// A lightweight struct representing a UI element for the current frame.
    /// This is filled each frame by the immediate-mode API and then used by layout and render.
    /// </summary>
    public struct UIElement
    {
        /// <summary>
        /// The node index this element corresponds to in the UiNode array.
        /// </summary>
        public int NodeIndex;

        /// <summary>
        /// Preliminary bounds (before layout). Will be overwritten by layout engine.
        /// </summary>
        public Rectangle Bounds;

        /// <summary>
        /// Visual properties: color, text, texture, etc.
        /// For markup elements, text is referenced via StringIndex into the global string table.
        /// For immediate-mode elements, text is stored in the frame arena via TextOffset/TextLength.
        /// </summary>
        public uint StringIndex; // 0 means no text from string table
        public int TextOffset;   // offset into frame text arena (if StringIndex == 0xFFFFFFFF)
        public int TextLength;   // length in chars (if StringIndex == 0xFFFFFFFF)
        // We'll use StringIndex == uint.MaxValue to indicate text comes from the arena.

        public uint Color; // ARGB
        // We'll add more properties as needed (e.g., texture, font size, etc.)

        // Flags: visible, interactive, etc.
        public bool Visible;
        public bool Interactive;

        /// <summary>
        /// Layout direction for this element (if it is a container). Determines how children are laid out.
        /// </summary>
        public LayoutDirection LayoutDirection;

        /// <summary>
        /// Gets the text as a ReadOnlySpan<char> from either the string table or the frame arena.
        /// </summary>
        public ReadOnlySpan<char> GetText(string[] stringTable, char[] textArena)
        {
            if (StringIndex == uint.MaxValue)
            {
                // Arena text
                if (TextOffset >= 0 && TextLength > 0 && textArena != null)
                {
                    return textArena.AsSpan(TextOffset, TextLength);
                }
                else
                {
                    return ReadOnlySpan<char>.Empty;
                }
            }
            else if (StringIndex > 0 && stringTable != null && StringIndex < stringTable.Length)
            {
                // String table text
                return stringTable[StringIndex];
            }
            else
            {
                return ReadOnlySpan<char>.Empty;
            }
        }

        /// <summary>
        /// Helper to set text from the frame arena (called by immediate-mode API).
        /// </summary>
        public void SetTextFromArena(int offset, int length)
        {
            StringIndex = uint.MaxValue;
            TextOffset = offset;
            TextLength = length;
        }

        /// <summary>
        /// Helper to set text from the string table (called during markup processing).
        /// </summary>
        public void SetTextFromTable(uint stringIndex)
        {
            StringIndex = stringIndex;
            TextOffset = 0;
            TextLength = 0;
        }
    }

    /// <summary>
    /// Layout direction for container elements.
    /// </summary>
    public enum LayoutDirection
    {
        None,
        Horizontal,
        Vertical
    }
}
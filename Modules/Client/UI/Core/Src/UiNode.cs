using System;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Represents a node in the UI tree.
    /// </summary>
    public struct UiNode
    {
        /// <summary>
        /// The type of the UI element (e.g., Button, Label).
        /// </summary>
        public UiTypeId Type;

        /// <summary>
        /// Offset in the PropsBlob where the properties for this node are stored.
        /// </summary>
        public ushort PropsOffset;

        /// <summary>
        /// Index of the first child node in the UiNode array.
        /// </summary>
        public ushort FirstChildIdx;

        /// <summary>
        /// Index of the next sibling node in the UiNode array.
        /// </summary>
        public ushort NextSiblingIdx;

        /// <summary>
        /// Flags indicating visibility, interactivity, etc.
        /// </summary>
        public byte Flags;
    }
}
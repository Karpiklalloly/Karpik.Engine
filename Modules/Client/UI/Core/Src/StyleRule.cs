using System;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Represents a style rule from USS: selector hash -> style properties blob.
    /// </summary>
    public struct StyleRule
    {
        /// <summary>
        /// Hash of the selector (e.g., class name).
        /// </summary>
        public ushort ClassHash;

        /// <summary>
        /// Blob of style properties (e.g., color, font size, etc.).
        /// The exact format is defined by the UI system and parsed from USS.
        /// </summary>
        public byte[] Properties;
    }
}
using System;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Represents a binding between a UI property and a getter/setter delegate.
    /// </summary>
    public struct Binding
    {
        /// <summary>
        /// Getter delegate: returns the current value of the bound property.
        /// </summary>
        public Delegate Getter;

        /// <summary>
        /// Setter delegate: sets the value of the bound property.
        /// </summary>
        public Delegate Setter;
    }
}
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public interface ICamera2D
{
    /// <summary>
    /// Camera position in world coordinates (meters)
    /// </summary>
    Vector2 Position { get; set; }
    
    /// <summary>
    /// Zoom: pixels per world unit (meter). Default: 50
    /// </summary>
    float Zoom { get; set; }
    
    /// <summary>
    /// Rotation in radians
    /// </summary>
    float Rotation { get; set; }
    
    /// <summary>
    /// Viewport size in pixels (set from renderer)
    /// </summary>
    Vector2 ViewportSize { get; set; }
    
    /// <summary>
    /// Convert world position to screen position (pixels)
    /// </summary>
    Vector2 WorldToScreen(Vector2 worldPosition);
    
    /// <summary>
    /// Convert screen position to world position (meters)
    /// </summary>
    Vector2 ScreenToWorld(Vector2 screenPosition);
}

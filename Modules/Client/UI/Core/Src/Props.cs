using System;
using System.Runtime.InteropServices;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Base for all property structs. Must be blittable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WindowProps
    {
        public uint StringIndex; // index into StringTable for title
        public Rectangle Bounds; // preliminary, will be overwritten by layout
        public uint Color; // background
        public ushort StyleClassIdx; // index into style rules
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ButtonProps
    {
        public uint StringIndex; // index into StringTable for text
        public Rectangle Bounds;
        public uint Color; // background
        public ushort BindingIdx; // index into Bindings array for two-way? maybe for label? we can use for command parameter.
        public ushort HandlerIdx; // index into ClickHandler array
        public ushort StyleClassIdx;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LabelProps
    {
        public uint StringIndex; // index into StringTable for text
        public Rectangle Bounds;
        public uint Color; // text color
        public ushort StyleClassIdx;
        public ushort FontSize; // in points
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InputFieldProps
    {
        public uint StringIndex; // index into StringTable for placeholder
        public Rectangle Bounds;
        public uint Color; // background
        public uint TextColor;
        public ushort BindingIdx; // index into Bindings for two-way string
        public ushort StyleClassIdx;
        public ushort MaxLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SliderProps
    {
        public Rectangle Bounds;
        public uint Color; // track
        public uint HandleColor;
        public ushort BindingIdx; // float value
        public ushort StyleClassIdx;
        public ushort Steps; // number of steps, 0 for continuous
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ProgressBarProps
    {
        public Rectangle Bounds;
        public uint Color; // background
        public uint FillColor;
        public ushort BindingIdx; // float 0..1
        public ushort StyleClassIdx;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ComboBoxProps
    {
        public uint StringIndex; // index into StringTable for selected text
        public Rectangle Bounds;
        public uint Color; // background
        public uint TextColor;
        public ushort BindingIdx; // maybe index of selected item
        public ushort StyleClassIdx;
        public ushort DropDownHeight; // in pixels
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LocProps
    {
        public uint StringIndex; // index into StringTable for localization key
        public Rectangle Bounds;
        public uint Color; // text color
        public ushort StyleClassIdx;
        public ushort FontSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SpacingProps
    {
        public Rectangle Bounds; // size of spacing
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WorldWindowProps
    {
        public Rectangle Bounds; // size in world units? we'll treat as local size
        // we need transform; we can store a matrix or position+rotation+scale.
        // For simplicity, we store a Matrix4x4 as 16 floats.
        public float M11, M12, M13, M14,
                     M21, M22, M23, M24,
                     M31, M32, M33, M34,
                     M41, M42, M43, M44;
        public uint Color; // background
        public ushort StyleClassIdx;
    }
}
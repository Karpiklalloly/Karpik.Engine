using System.Drawing;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public static class SystemToVeldridExtensions
{
    extension(Color color)
    {
        public RgbaFloat VeldridFloat => new RgbaFloat(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    extension(RgbaFloat color)
    {
        public Color Color => Color.FromArgb(
            (byte)(color.A * 255),
            (byte)(color.R * 255),
            (byte)(color.G * 255),
            (byte)(color.B * 255));
    }
}
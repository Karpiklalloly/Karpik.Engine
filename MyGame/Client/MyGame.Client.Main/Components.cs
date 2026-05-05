using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;

namespace Karpik.Engine.MyGame.Client.Main;

public struct CameraHolder : IEcsComponent
{
    public Camera2D Camera;
}
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Raylib_cs;

using R = Raylib_cs.Raylib;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibWindow : IWindow
{
    public bool IsResized()
    {
        return R.IsWindowResized();
    }

    public float GetWidth()
    {
        return R.GetRenderWidth();
    }

    public float GetHeight()
    {
        return R.GetRenderHeight();
    }

    public int GetScreenWidth()
    {
        return R.GetScreenWidth();
    }

    public int GetScreenHeight()
    {
        return R.GetScreenHeight();
    }

    public int GetKeyPressed()
    {
        return R.GetKeyPressed();
    }

    public char GetCharPressed()
    {
        return (char)R.GetCharPressed();
    }

    public bool IsMouseButtonPressed(int button)
    {
        return R.IsMouseButtonPressed((MouseButton)button);
    }

    public bool IsMouseButtonReleased(int button)
    {
        return R.IsMouseButtonReleased((MouseButton)button);
    }

    public bool IsMouseButtonDown(int button)
    {
        return R.IsMouseButtonDown((MouseButton)button);
    }

    public bool IsKeyPressed(int key)
    {
        return R.IsKeyPressed((KeyboardKey)key);
    }

    public bool IsKeyReleased(int key)
    {
        return R.IsKeyReleased((KeyboardKey)key);
    }

    public bool IsKeyDown(int key)
    {
        return R.IsKeyDown((KeyboardKey)key);
    }

    public bool IsKeyUp(int key)
    {
        return R.IsKeyUp((KeyboardKey)key);
    }

    public Vector2 GetMousePosition()
    {
        return R.GetMousePosition();
    }

    public Vector2 GetMouseDelta()
    {
        return R.GetMouseDelta();
    }

    public void DisableCursor()
    {
        R.DisableCursor();
    }

    public void EnableCursor()
    {
        R.EnableCursor();
    }

    public void Init(int width, int height, string title)
    {
        R.InitWindow(width, height, title);
        R.SetExitKey(KeyboardKey.Null);
    }

    public void SetWindowState(WindowFlags flags)
    {
        R.SetWindowState((ConfigFlags)flags);
    }

    public void SetWindowMinSize(int width, int height)
    {
        R.SetWindowMinSize(width, height);
    }

    public void SetTargetFPS(int fps)
    {
        R.SetTargetFPS(fps);
    }
}
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public interface IWindow
{
    public bool IsResized();
    
    public float GetWidth();
    public float GetHeight();
    
    public int GetScreenWidth();
    public int GetScreenHeight();

    public int GetKeyPressed();
    public char GetCharPressed();
    public bool IsMouseButtonPressed(int button);
    public bool IsMouseButtonReleased(int button);
    public bool IsMouseButtonDown(int button);
    public bool IsKeyPressed(int key);
    public bool IsKeyReleased(int key);
    public bool IsKeyDown(int key);
    public bool IsKeyUp(int key);

    public Vector2 GetMousePosition();
    public Vector2 GetMouseDelta();
    public void DisableCursor();
    public void EnableCursor();
    
    void Init(int width, int height, string title);
    void SetWindowState(WindowFlags flags);
    void SetWindowMinSize(int width, int height);
    void SetTargetFPS(int fps);
}
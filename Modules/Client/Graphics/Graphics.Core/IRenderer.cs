using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public interface IRenderer
{
    public ICamera MainCamera3D { get; }
    public ICamera2D MainCamera2D { get; }
    public ICamera CreateCamera3D();
    public ICamera2D CreateCamera2D();
    
    public void BeginScissorMode(RectangleF scissor);
    public void EndScissorMode();
    
    public Vector2 MeasureText(IFont font, string text, float fontSize, float spacing);
    
    public void DrawRectangleLines(RectangleF rectangle, float thickness, Color color);
    
    public void DrawRectangleLinesRounded(RectangleF rectangle, float roundness, int segments, Color color);
    public void DrawRectangleLinesRounded(RectangleF rectangle, float roundness, int segments, int thickness, Color color);
    
    public void DrawRectangle(RectangleF rectangle, Color color);
    public void DrawRectangle(RectangleF rectangle, Vector2 origin, float rotation, Color color);
    
    public void DrawSphere(Vector3 center, float radius, Color color);
    
    public void DrawRectangleRounded(RectangleF rectangle, float roundness, int segments, Color color);
    
    public void DrawText(string text, Vector2 position, float fontSize, Color color);
    public void DrawText(IFont font, string text, Vector2 position, float fontSize, float spacing, Color color);
    public void DrawText(IFont font, string text, Vector2 position, Vector2 origin, float rotation, float fontSize, float spacing, Color color);
    public void DrawTexture(ITexture2D texture, RectangleF source, Vector2 position, Color color);
    
    public void DrawTexture(ITexture2D texture, Vector2 position, Color color);
    public void DrawTexture(ITexture2D texture, Vector2 position, float rotation, float scale, Color color);
    public void DrawTexture(ITexture2D texture, RectangleF source, RectangleF destination, Vector2 origin, float rotation, Color color);
    
    public void BeginTextureMode(IRenderTexture2D renderTexture);
    public void EndTextureMode();
    
    public void BeginMode3D(ICamera camera);
    public void End3DMode3D();
    
    public void BeginMode2D(ICamera2D camera);
    public void End2DMode();
    
    public void BeginDrawing();
    public void ClearBackground(Color color);
    public void EndDrawing();
    
    public IRenderTexture2D LoadRenderTexture(int width, int height);
    public void UnloadTexture(ITexture2D texture);
    public void UnloadRenderTexture(IRenderTexture2D texture);
    
    public RectangleF GetScreenRectangle();

    public IFont GetFontDefault();
    public int[] LoadCodepoints(string codes, ref int count);
    public IFont LoadFont(string fileName, int fontSize, int[] codepoints, int codepointCount);
    public bool IsFontValid(IFont font);
    public int GetFPS();
    public bool WindowShouldClose();
}
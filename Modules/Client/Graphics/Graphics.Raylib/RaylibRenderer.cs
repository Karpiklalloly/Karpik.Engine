using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Raylib_cs;
using Color = System.Drawing.Color;
using RectangleF = System.Drawing.RectangleF;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibRenderer : IRenderer
{
    public ICamera MainCamera3D { get; }
    public ICamera2D MainCamera2D { get; }

    public RaylibRenderer()
    {
        MainCamera3D = CreateCamera3D();
        MainCamera2D = CreateCamera2D();
    }

    public ICamera CreateCamera3D()
    {
        return new RaylibCamera();
    }

    public ICamera2D CreateCamera2D()
    {
        return new RaylibCamera2D();
    }

    public void BeginScissorMode(RectangleF scissor)
    {
        Raylib.BeginScissorMode((int)scissor.X, (int)scissor.Y, (int)scissor.Width, (int)scissor.Height);
    }

    public void EndScissorMode()
    {
        Raylib.EndScissorMode();
    }

    public Vector2 MeasureText(IFont font, string text, float fontSize, float spacing)
    {
        return Raylib.MeasureTextEx(((RaylibFont)font).Font, text, fontSize, spacing);
    }

    public void DrawRectangleLines(RectangleF rectangle, float thickness, Color color)
    {
        Raylib.DrawRectangleLinesEx(rectangle.Raylib, thickness, color.Raylib);
    }

    public void DrawRectangleLinesRounded(RectangleF rectangle, float roundness, int segments, Color color)
    {
        Raylib.DrawRectangleRoundedLines(rectangle.Raylib, roundness, segments, color.Raylib);
    }

    public void DrawRectangleLinesRounded(RectangleF rectangle, float roundness, int segments, int thickness, Color color)
    {
        Raylib.DrawRectangleRoundedLinesEx(rectangle.Raylib, roundness, segments, thickness, color.Raylib);
    }

    public void DrawRectangle(RectangleF rectangle, Color color)
    {
        Raylib.DrawRectangleRec(rectangle.Raylib, color.Raylib);
    }

    public void DrawRectangle(RectangleF rectangle, Vector2 origin, float rotation, Color color)
    {
        Raylib.DrawRectanglePro(rectangle.Raylib, origin, rotation, color.Raylib);
    }

    public void DrawSphere(Vector3 center, float radius, Color color)
    {
        Raylib.DrawSphere(center, radius, color.Raylib);
    }

    public void DrawRectangleRounded(RectangleF rectangle, float roundness, int segments, Color color)
    {
        Raylib.DrawRectangleRounded(rectangle.Raylib, roundness, segments, color.Raylib);
    }

    public void DrawText(string text, Vector2 position, float fontSize, Color color)
    {
        Raylib.DrawTextEx(Raylib.GetFontDefault(), text, position, fontSize, 0, color.Raylib);
    }

    public void DrawText(IFont font, string text, Vector2 position, float fontSize, float spacing, Color color)
    {
        Raylib.DrawTextEx(((RaylibFont)font).Font, text, position, fontSize, spacing, color.Raylib);
    }

    public void DrawText(IFont font, string text, Vector2 position, Vector2 origin, float rotation, float fontSize, float spacing,
        Color color)
    {
        Raylib.DrawTextPro(((RaylibFont)font).Font, text, position, origin, rotation, fontSize, spacing, color.Raylib);
    }

    public void DrawTexture(ITexture2D texture, Vector2 position, Color color)
    {
        Raylib.DrawTexture(((RaylibTexture2D)texture).Texture, (int)position.X, (int)position.Y, color.Raylib);
    }

    public void DrawTexture(ITexture2D texture, Vector2 position, float rotation, float scale, Color color)
    {
        Raylib.DrawTextureEx(((RaylibTexture2D)texture).Texture, position, rotation, scale, color.Raylib);
    }

    public void DrawTexture(ITexture2D texture, RectangleF source, RectangleF destination, Vector2 origin, float rotation,
        Color color)
    {
        Raylib.DrawTexturePro(
            texture.Raylib,
            source.Raylib,
            destination.Raylib,
            origin,
            rotation,
            color.Raylib
        );
    }

    public void DrawTexture(ITexture2D texture, RectangleF source, Vector2 position, Color color)
    {
        Raylib.DrawTextureRec(((RaylibTexture2D)texture).Texture, source.Raylib, position, color.Raylib);
    }

    public void BeginTextureMode(IRenderTexture2D renderTexture)
    {
        Raylib.BeginTextureMode(((RaylibRenderTexture2D)renderTexture).RenderTexture);
    }

    public void EndTextureMode()
    {
        Raylib.EndTextureMode();
    }

    public void BeginMode3D(ICamera camera)
    {
        Raylib.BeginMode3D(camera.Raylib3D);
    }

    public void End3DMode3D()
    {
        Raylib.EndMode3D();
    }

    public void BeginMode2D(ICamera2D camera)
    {
        Raylib.BeginMode2D(camera.Raylib2D);
    }

    public void End2DMode()
    {
        Raylib.EndMode2D();
    }

    public void BeginDrawing()
    {
        Raylib.BeginDrawing();
    }

    public void ClearBackground(Color color)
    {
        Raylib.ClearBackground(color.Raylib);
    }

    public void EndDrawing()
    {
        Raylib.EndDrawing();
    }
    
    public IRenderTexture2D LoadRenderTexture(int width, int height)
    {
        return new RaylibRenderTexture2D()
        {
            RenderTexture = Raylib.LoadRenderTexture(width, height)
        };
    }

    public void UnloadTexture(ITexture2D texture)
    {
        Raylib.UnloadTexture(((RaylibTexture2D)texture).Texture);
    }

    public void UnloadRenderTexture(IRenderTexture2D texture)
    {
        Raylib.UnloadRenderTexture(((RaylibRenderTexture2D)texture).RenderTexture);
    }

    public RectangleF GetScreenRectangle()
    {
        return new RectangleF(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    }

    public IFont GetFontDefault()
    {
        return new RaylibFont()
        {
            Font = Raylib.GetFontDefault()
        };
    }

    public int[] LoadCodepoints(string codes, ref int count)
    {
        return Raylib.LoadCodepoints(codes, ref count);
    }

    public IFont LoadFont(string fileName, int fontSize, int[] codepoints, int codepointCount)
    {
        return new RaylibFont()
        {
            Font = Raylib.LoadFontEx(fileName, fontSize, codepoints, codepointCount)
        };
    }

    public bool IsFontValid(IFont font)
    {
        return Raylib.IsFontValid(((RaylibFont)font).Font);
    }

    public int GetFPS()
    {
        return Raylib.GetFPS();
    }

    public bool WindowShouldClose()
    {
        return Raylib.WindowShouldClose();
    }
}
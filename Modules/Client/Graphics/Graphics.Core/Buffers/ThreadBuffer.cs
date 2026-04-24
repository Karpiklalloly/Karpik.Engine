using System.Runtime.CompilerServices;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ThreadBuffer(int frameId) : ICommandBuffer
{
    public int FrameId { get; } = frameId;
    
    public ReadOnlySpan<DrawRectCmd> GetRectCommands() => _rects.AsSpan(0, _rectCount);
    public ReadOnlySpan<DrawTextureCmd> GetTextureCommands() => _textures.AsSpan(0, _textureCount);
    public ReadOnlySpan<DrawTextCmd> GetTextCommands() => _texts.AsSpan(0, _textCount);

    private DrawRectCmd[] _rects = new DrawRectCmd[256];
    private int _rectCount;

    private DrawTextureCmd[] _textures = new DrawTextureCmd[128];
    private int _textureCount;

    private DrawTextCmd[] _texts = new DrawTextCmd[64];
    private int _textCount;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in DrawRectCmd cmd)
    {
        if (_rectCount >= _rects.Length) Resize(ref _rects);
        _rects[_rectCount++] = cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in DrawTextureCmd cmd)
    {
        if (_textureCount >= _textures.Length) Resize(ref _textures);
        _textures[_textureCount++] = cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in DrawTextCmd cmd)
    {
        if (_textCount >= _texts.Length) Resize(ref _texts);
        _texts[_textCount++] = cmd;
    }

    public void Clear()
    {
        _rectCount = 0;
        _textureCount = 0;
        _textCount = 0;
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Resize<T>(ref T[] array)
    {
        Array.Resize(ref array, array.Length * 2);
    }
}
using System.Runtime.CompilerServices;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ThreadBuffer : ICommandBuffer, IOrderedCommandBuffer
{
    public int FrameId { get; private set; } = -1;
    internal bool AllowResize { get; set; } = true;
    
    public ReadOnlySpan<DrawRectCmd> GetRectCommands() => _rects.AsSpan(0, _rectCount);
    public ReadOnlySpan<DrawTextureCmd> GetTextureCommands() => _textures.AsSpan(0, _textureCount);
    public ReadOnlySpan<DrawTextCmd> GetTextCommands() => _texts.AsSpan(0, _textCount);
    ReadOnlySpan<DrawCommand> IOrderedCommandBuffer.GetCommands() => _commands.AsSpan(0, _commandCount);

    private DrawRectCmd[] _rects = new DrawRectCmd[256];
    private int _rectCount;

    private DrawTextureCmd[] _textures = new DrawTextureCmd[128];
    private int _textureCount;

    private DrawTextCmd[] _texts = new DrawTextCmd[64];
    private int _textCount;

    private char[] _textChars = new char[4096];
    private int _textCharCount;

    private DrawCommand[] _commands = new DrawCommand[512];
    private int _commandCount;

    internal void EnsureCapacity(int rects, int textures, int texts, int commands)
    {
        EnsureCapacity(rects, textures, texts, commands, 0);
    }

    internal void EnsureCapacity(int rects, int textures, int texts, int commands, int textChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rects);
        ArgumentOutOfRangeException.ThrowIfNegative(textures);
        ArgumentOutOfRangeException.ThrowIfNegative(texts);
        ArgumentOutOfRangeException.ThrowIfNegative(commands);
        ArgumentOutOfRangeException.ThrowIfNegative(textChars);

        EnsureCapacity(ref _rects, rects);
        EnsureCapacity(ref _textures, textures);
        EnsureCapacity(ref _texts, texts);
        EnsureCapacity(ref _commands, commands);
        EnsureCapacity(ref _textChars, textChars);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in DrawRectCmd cmd)
    {
        if (_rectCount >= _rects.Length) ResizeOrThrow(ref _rects);
        if (_commandCount >= _commands.Length) ResizeOrThrow(ref _commands);
        _commands[_commandCount++] = new DrawCommand(DrawCommandType.Rect, _rectCount);
        _rects[_rectCount++] = cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in DrawTextureCmd cmd)
    {
        if (_textureCount >= _textures.Length) ResizeOrThrow(ref _textures);
        if (_commandCount >= _commands.Length) ResizeOrThrow(ref _commands);
        _commands[_commandCount++] = new DrawCommand(DrawCommandType.Texture, _textureCount);
        _textures[_textureCount++] = cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in DrawTextCmd cmd)
    {
        if (_textCount >= _texts.Length) ResizeOrThrow(ref _texts);
        if (_commandCount >= _commands.Length) ResizeOrThrow(ref _commands);
        _commands[_commandCount++] = new DrawCommand(DrawCommandType.Text, _textCount);
        _texts[_textCount++] = cmd;
    }

    internal ReadOnlyMemory<char> CopyText(ReadOnlySpan<char> text)
    {
        int start = _textCharCount;
        int required = checked(start + text.Length);
        if (required > _textChars.Length)
        {
            EnsureCapacityOrThrow(ref _textChars, required);
        }

        text.CopyTo(_textChars.AsSpan(start));
        _textCharCount = required;
        return new ReadOnlyMemory<char>(_textChars, start, text.Length);
    }

    internal void BeginFrame(int frameId)
    {
        FrameId = frameId;
        Clear();
    }

    public void Clear()
    {
        _rectCount = 0;
        _textureCount = 0;
        _textCount = 0;
        _textCharCount = 0;
        _commandCount = 0;
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Resize<T>(ref T[] array)
    {
        Array.Resize(ref array, array.Length * 2);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResizeOrThrow<T>(ref T[] array)
    {
        if (!AllowResize)
        {
            throw new InvalidOperationException(
                $"Graphics command buffer capacity exceeded for {typeof(T).Name}. Call GraphicsContext.EnsureThreadBufferCapacity during warm-up.");
        }

        Resize(ref array);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureCapacityOrThrow<T>(ref T[] array, int capacity)
    {
        if (!AllowResize)
        {
            throw new InvalidOperationException(
                $"Graphics command buffer capacity exceeded for {typeof(T).Name}. Call GraphicsContext.EnsureThreadBufferCapacity during warm-up.");
        }

        EnsureCapacity(ref array, capacity);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureCapacity<T>(ref T[] array, int capacity)
    {
        if (capacity <= array.Length)
        {
            return;
        }

        int newLength = array.Length;
        while (newLength < capacity)
        {
            if (newLength > Array.MaxLength / 2)
            {
                throw new OutOfMemoryException($"Requested graphics command buffer capacity is too large: {capacity}.");
            }

            newLength *= 2;
        }

        Array.Resize(ref array, newLength);
    }
}

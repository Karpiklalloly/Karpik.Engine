namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public interface IGraphicsDevice : IDisposable
{
    IFramebuffer SwapchainFramebuffer { get; }

    // Фабричные методы (Cold Path)
    ICommandList CreateCommandList();
    IDeviceBuffer CreateBuffer(in BufferDescription description);
    ITexture CreateTexture(uint width, uint height, PixelFormat format);
    IPipeline CreateGraphicsPipeline(in PipelineDescription description);

    // Выполнение команд (Hot Path).
    // Передаем Span, чтобы можно было засабмитить несколько CommandList без создания массива
    void Submit(ICommandList commandList);
    void Submit(ReadOnlySpan<ICommandList> commandLists); 

    // Смена кадра
    void SwapBuffers();
    
    // Синхронизация CPU и GPU (Ожидание завершения работы видеокарты)
    void WaitForIdle();
}
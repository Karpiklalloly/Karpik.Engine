namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public interface ICommandList : IDisposable
{
    void Begin();
    void End();

    void SetFramebuffer(IFramebuffer framebuffer);
    void SetPipeline(IPipeline pipeline);
    
    // Передаем цвета через ref/in, чтобы не копировать структуру
    void ClearColorTarget(uint index, in RgbaFloat clearColor);
    void ClearDepthTarget(float depth, byte stencil = 0);

    void SetVertexBuffer(uint slot, IDeviceBuffer buffer, uint offset = 0);
    void SetIndexBuffer(IDeviceBuffer buffer, PixelFormat format, uint offset = 0);
    void SetGraphicsResourceSet(uint slot, IResourceSet resourceSet);

    void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart);
    void DrawIndexed(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart);

    // ВЫСОКОПРОИЗВОДИТЕЛЬНАЯ ЗАПИСЬ ДАННЫХ
    // Работает напрямую с unmanaged памятью. Без боксинга!
    void UpdateBuffer<T>(IDeviceBuffer buffer, uint bufferOffsetInBytes, in T data) where T : unmanaged;
    
    // Zero-allocation массовая загрузка данных (Span не аллоцирует память в куче)
    void UpdateBuffer<T>(IDeviceBuffer buffer, uint bufferOffsetInBytes, ReadOnlySpan<T> data) where T : unmanaged;
}
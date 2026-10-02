using Autofac;

namespace Karpik.Engine.Client.Graphics.Core;

public interface IGraphicsBackend : IDisposable, IStartable
{
    bool IsHeadless { get; }

    void BeginFrame();

    void BeginMerge(in Camera2D camera);

    void SubmitScene();

    void UpdateImGui(out bool wantsMouse, out bool wantsKeyboard, out bool wantsText);

    void RenderImGui();

    void SwapBuffers();
}

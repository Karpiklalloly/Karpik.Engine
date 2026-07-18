using Karpik.Engine.Core;

[Module]
public class GameInstaller : IInstaller
{
    public string Name => "KarpikGame.Shared";

    public void OnRegisterServices(IServiceRegister services, IServiceContainer container)
    {
        Console.WriteLine($"[Game] {nameof(GameInstaller)} registered");
    }
}

using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Network.Core;

[Export(typeof(NetworkConfig))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class NetworkConfig
{
    public string Address { get; set; } = "localhost";
    public int Port { get; set; } = 9051;
    public string Key { get; set; } = "Karpik";
}

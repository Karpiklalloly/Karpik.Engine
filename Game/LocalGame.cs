using Karpik.Engine.Client;
using Karpik.Engine.Server;

namespace Karpik.Game;

public class LocalGame : IDisposable
{
    private Client _client = new Client();
    private Server _server = new Server();

    private Thread _serverThread;
    private Thread _clientThread;
    
    private bool _isRunning = false;
    
    public void Start()
    {
        _isRunning = true;

        _serverThread = new Thread(() =>
        {
            _server.Init();
            _server.Run(in _isRunning);
            _isRunning = false;
        });
        
        _clientThread = new Thread(() =>
        {
            _client.Init();
            _client.Run(in _isRunning);
            _isRunning = false;
        });
        
        _serverThread.Start();
        _clientThread.Start();
        
        _serverThread.Join();
        _clientThread.Join();
    }

    public void Dispose()
    {
        
    }
}
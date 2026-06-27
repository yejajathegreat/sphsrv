using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SphereServer.Database;

namespace SphereServer.Network;

/// <summary>
/// Main TCP server. Accepts connections, assigns player indices, manages sessions.
/// </summary>
public class SphereGameServer
{
    private const int DefaultPort = 25860;

    public PlayerDb Database { get; }

    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<ushort, ClientSession> _clients = new();
    private ushort _nextPlayerIndex = 1;
    private readonly CancellationTokenSource _cts = new();

    public SphereGameServer(int port = DefaultPort, string dbPath = "sphere.db")
    {
        Database = new PlayerDb(dbPath);
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"[SERVER] Sphere server listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");
        Console.WriteLine($"[SERVER] Waiting for connections...");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
                var playerIndex = _nextPlayerIndex++;

                var session = new ClientSession(tcpClient, playerIndex, this);
                _clients[playerIndex] = session;

                Console.WriteLine($"[SERVER] New connection #{playerIndex}, total clients: {_clients.Count}");

                // Run client session in background
                _ = Task.Run(() => session.RunAsync(), _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _listener.Stop();
        }
    }

    public void RemoveClient(ClientSession session)
    {
        _clients.TryRemove(session.PlayerIndex, out _);
        Console.WriteLine($"[SERVER] Client #{session.PlayerIndex} removed, total clients: {_clients.Count}");
    }

    public IEnumerable<ClientSession> GetIngameClients() =>
        _clients.Values.Where(c => c.State == ClientState.InGame);

    public ClientSession? GetClient(ushort playerIndex) =>
        _clients.GetValueOrDefault(playerIndex);

    public async Task BroadcastAsync(byte[] data, ushort? excludeIndex = null)
    {
        foreach (var client in GetIngameClients())
        {
            if (client.PlayerIndex != excludeIndex)
            {
                await client.SendAsync(data);
            }
        }
    }

    public void Stop()
    {
        Console.WriteLine("[SERVER] Shutting down...");
        _cts.Cancel();

        foreach (var client in _clients.Values)
        {
            client.Disconnect();
        }

        Database.Dispose();
        Console.WriteLine("[SERVER] Stopped.");
    }
}

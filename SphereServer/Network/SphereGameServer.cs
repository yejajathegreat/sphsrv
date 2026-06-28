using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SphereServer.Database;

namespace SphereServer.Network;

/// <summary>
/// Main TCP server. Accepts connections, assigns player indices, manages sessions.
/// Listens on both game port (25860) and update port (25859) since the client
/// may connect to either depending on configuration.
/// </summary>
public class SphereGameServer
{
    private const int DefaultPort = 25860;
    private const int DefaultUpdatePort = 25859;

    public PlayerDb Database { get; }

    private readonly TcpListener _listener;
    private readonly TcpListener _updateListener;
    private readonly ConcurrentDictionary<ushort, ClientSession> _clients = new();
    private ushort _nextPlayerIndex = 1;
    private readonly CancellationTokenSource _cts = new();

    public SphereGameServer(int port = DefaultPort, string dbPath = "sphere.db")
    {
        Database = new PlayerDb(dbPath);
        _listener = new TcpListener(IPAddress.Any, port);
        _updateListener = new TcpListener(IPAddress.Any, port == DefaultPort ? DefaultUpdatePort : port - 1);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        _updateListener.Start();
        var mainPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var updPort = ((IPEndPoint)_updateListener.LocalEndpoint).Port;
        Console.WriteLine($"[SERVER] Listening on ports {mainPort} (game) and {updPort} (update/auth)");
        Console.WriteLine($"[SERVER] Waiting for connections...");

        try
        {
            // Accept on both ports in parallel
            var mainTask = AcceptLoop(_listener, "GAME");
            var updTask = AcceptLoop(_updateListener, "AUTH");
            await Task.WhenAny(mainTask, updTask);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _listener.Stop();
            _updateListener.Stop();
        }
    }

    private async Task AcceptLoop(TcpListener listener, string tag)
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var tcpClient = await listener.AcceptTcpClientAsync(_cts.Token);
            var playerIndex = _nextPlayerIndex++;
            var endpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "?";
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Console.WriteLine($"[SERVER] New connection #{playerIndex} on port {port} ({tag}) from {endpoint}");

            var session = new ClientSession(tcpClient, playerIndex, this);
            _clients[playerIndex] = session;

            _ = Task.Run(() => session.RunAsync(), _cts.Token);
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

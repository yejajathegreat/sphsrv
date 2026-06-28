using System.Net;
using System.Net.Sockets;
using System.Text;
using SphereServer.Database;

namespace SphereServer.Network;

/// <summary>
/// Main TCP server. Ported from knelse Server.cs.
/// Accepts connections on port 25860, assigns player index starting at 0x4f6f.
/// Each client is handled in its own task via ClientSession.
/// </summary>
public class SphereGameServer
{
    public PlayerDb Database { get; }
    public static Encoding Win1251 = null!;

    private readonly int _port;
    private TcpListener? _listener;
    private int _playerIndex = 0x4F6F;
    private int _playerCount;
    private readonly CancellationTokenSource _cts = new();

    public SphereGameServer(int port = 25860, string dbPath = "sphere.db")
    {
        _port = port;
        Database = new PlayerDb(dbPath);
    }

    private ushort GetNewPlayerIndex()
    {
        if (_playerIndex > 65535)
            throw new ArgumentException("Reached max number of connections");
        return (ushort)_playerIndex;
    }

    public async Task StartAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
        }
        catch (SocketException se)
        {
            Console.WriteLine(se.Message);
            Environment.Exit(se.ErrorCode);
            return;
        }

        Console.WriteLine($"Server up on port {_port}, waiting for connections...");

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var currentPlayerIndex = GetNewPlayerIndex();
                var session = new ClientSession(client, currentPlayerIndex, this);
#pragma warning disable CS4014
                Task.Run(() => session.HandleClientAsync());
#pragma warning restore CS4014
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }

    public void IncrementPlayerCount() => Interlocked.Increment(ref _playerCount);
    public void DecrementPlayerCount() => Interlocked.Decrement(ref _playerCount);

    public void Stop()
    {
        Console.WriteLine("[SERVER] Shutting down...");
        _cts.Cancel();
        _listener?.Stop();
        Database.Dispose();
        Console.WriteLine("[SERVER] Stopped.");
    }
}

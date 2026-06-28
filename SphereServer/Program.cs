using System.Text;
using SphereServer.Network;

Console.WriteLine("=== Sphere Server Emulator (knelse port) ===");
Console.WriteLine();

int port = 25860;
string dbPath = "sphere.db";

// Parse command line args
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" or "-p" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;
        case "--db" when i + 1 < args.Length:
            dbPath = args[++i];
            break;
    }
}

var server = new SphereGameServer(port, dbPath);

// Graceful shutdown on Ctrl+C
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    server.Stop();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => server.Stop();

await server.StartAsync();

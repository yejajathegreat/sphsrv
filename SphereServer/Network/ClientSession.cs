using System.Net.Sockets;
using SphereServer.Database;
using SphereServer.Helpers;
using SphereServer.Protocol;

namespace SphereServer.Network;

/// <summary>
/// Represents one connected client. Manages the full state machine:
/// Handshake -> ServerCredentials -> Login -> CharacterSelect -> InGame
/// </summary>
public class ClientSession
{
    private const int BufferSize = 4096;

    public ushort PlayerIndex { get; }
    public ClientState State { get; private set; } = ClientState.Connected;
    public PlayerRecord? Player { get; private set; }
    public CharacterRecord? ActiveCharacter { get; private set; }

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly SphereGameServer _server;
    private readonly CancellationTokenSource _cts = new();

    private DateTime _lastPing3s = DateTime.UtcNow;
    private DateTime _lastPing6s = DateTime.UtcNow;
    private DateTime _lastPing15s = DateTime.UtcNow;

    // Ping/pong state
    private bool _pingShouldXorTopBit;
    private ushort _pingCounter = 0xE001;

    public ClientSession(TcpClient tcp, ushort playerIndex, SphereGameServer server)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _server = server;
        PlayerIndex = playerIndex;
    }

    public async Task RunAsync()
    {
        var endpoint = _tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Console.WriteLine($"[CLIENT {PlayerIndex}] Connected from {endpoint}");

        try
        {
            // Step 1: Handshake - send ReadyToLoadInitialData immediately
            await SendAsync(CommonPackets.ReadyToLoadInitialData);
            State = ClientState.WaitingForInitialData;
            Console.WriteLine($"[CLIENT {PlayerIndex}] Handshake sent");

            var buffer = new byte[BufferSize];

            while (!_cts.Token.IsCancellationRequested && _tcp.Connected)
            {
                await SendKeepaliveIfNeeded();

                if (!_stream.DataAvailable)
                {
                    await Task.Delay(10, _cts.Token);
                    continue;
                }

                int bytesRead = await _stream.ReadAsync(buffer, 0, BufferSize, _cts.Token);
                if (bytesRead == 0) break;

                var data = buffer[..bytesRead];
                Console.WriteLine($"[CLIENT {PlayerIndex}] << {bytesRead}b state={State}");

                await ProcessSubpackets(data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT {PlayerIndex}] Error: {ex.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    private async Task ProcessSubpackets(byte[] data)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (i + 1 >= data.Length) break;
            int packetLength = data[i] + data[i + 1] * 256;
            if (packetLength <= 0 || i + packetLength > data.Length)
            {
                await HandlePacket(data[i..]);
                return;
            }

            await HandlePacket(data[i..(i + packetLength)]);
            i += packetLength;
        }
    }

    private async Task HandlePacket(byte[] rawData)
    {
        switch (State)
        {
            case ClientState.WaitingForInitialData:
                await HandleInitialData(rawData);
                break;
            case ClientState.WaitingForLogin:
                await HandleLogin(rawData);
                break;
            case ClientState.WaitingForCharacterSelect:
                await HandleCharacterSelect(rawData);
                break;
            case ClientState.WaitingForIngameAck:
                await HandleIngameAck(rawData);
                break;
            case ClientState.InGame:
                await HandleIngamePacket(rawData);
                break;
        }
    }

    // ===== STEP 2: Client responded to handshake =====
    private async Task HandleInitialData(byte[] data)
    {
        Console.WriteLine($"[CLIENT {PlayerIndex}] Initial data response, sending credentials");
        await Task.Delay(100);
        await SendAsync(CommonPackets.ServerCredentials(PlayerIndex));
        await SendAsync(CommonPackets.TransmissionEnd);
        State = ClientState.WaitingForLogin;
    }

    // ===== STEP 3: Login =====
    private async Task HandleLogin(byte[] rawData)
    {
        Console.WriteLine($"[CLIENT {PlayerIndex}] Login raw ({rawData.Length}b): {ByteHelper.ToHex(rawData)}");
        var decoded = PacketCodec.DecodeClientPacket(rawData);
        Console.WriteLine($"[CLIENT {PlayerIndex}] Login decoded ({decoded.Length}b): {ByteHelper.ToHex(decoded)}");
        var (login, password) = LoginDecoder.Decode(decoded);

        if (string.IsNullOrEmpty(login))
        {
            Console.WriteLine($"[CLIENT {PlayerIndex}] Login decode failed");
            await SendAsync(CommonPackets.CannotConnect(PlayerIndex));
            Disconnect();
            return;
        }

        Console.WriteLine($"[CLIENT {PlayerIndex}] Login: [{login}]");
        Player = _server.Database.Login(login, password);

        if (Player == null)
        {
            await SendAsync(CommonPackets.CannotConnect(PlayerIndex));
            await SendAsync(CommonPackets.TransmissionEnd);
            Disconnect();
            return;
        }

        // Send character select screen
        await SendAsync(CommonPackets.CharacterSelectStartData(PlayerIndex));

        // Send 3 character slots
        var characters = _server.Database.GetCharacters(Player.CharacterIds);
        for (int i = 0; i < 3; i++)
        {
            if (i < characters.Count)
            {
                var charBytes = CharacterSerializer.ToCharacterListBytes(characters[i], PlayerIndex);
                await SendAsync(charBytes);
            }
            else
            {
                // Empty slot = CreateNewCharacterData template
                await SendAsync(CommonPackets.CreateNewCharacterData(PlayerIndex));
            }
        }

        await SendAsync(CommonPackets.TransmissionEnd);
        State = ClientState.WaitingForCharacterSelect;
        Console.WriteLine($"[CLIENT {PlayerIndex}] Character list sent ({characters.Count} chars)");
    }

    // ===== STEP 4: Character select/create/delete =====
    private async Task HandleCharacterSelect(byte[] rawData)
    {
        var decoded = PacketCodec.DecodeClientPacket(rawData);
        Console.WriteLine($"[CLIENT {PlayerIndex}] CharSelect: 0x{decoded[0]:X2} len={decoded.Length}");

        // Delete character
        if (decoded.Length >= 18 && decoded[0] == 0x2A)
        {
            int charIndex = decoded[17] / 4 - 1;
            Console.WriteLine($"[CLIENT {PlayerIndex}] Delete slot {charIndex} (not implemented)");
            return;
        }

        // Create new character
        if (decoded.Length >= 17 && decoded[0] >= 0x1B &&
            decoded.Length > 16 && decoded[13] == 0x08 && decoded[14] == 0x40 &&
            decoded[15] == 0x80 && decoded[16] == 0x05)
        {
            // Decode name (5-bit shifted encoding)
            string charName = DecodeCharacterName(decoded);
            Console.WriteLine($"[CLIENT {PlayerIndex}] Create character: [{charName}]");

            if (_server.Database.IsNameTaken(charName))
            {
                await SendAsync(CommonPackets.NameAlreadyExists(PlayerIndex));
                return;
            }

            await SendAsync(CommonPackets.NameCheckPassed(PlayerIndex));

            // Create in DB with default spawn point (Shipstown area)
            var newChar = _server.Database.CreateCharacter(charName, Player!.Id);
            newChar.X = 2614; newChar.Y = 157.8f; newChar.Z = 1293; // Shipstown
            newChar.Hp = 200; newChar.MaxHp = 200;
            newChar.Mp = 200; newChar.MaxMp = 200;
            _server.Database.SaveCharacter(newChar);

            // Send game data and enter world
            ActiveCharacter = newChar;
            var gameData = CharacterSerializer.ToGameDataBytes(newChar, PlayerIndex);
            await SendAsync(gameData);
            await SendAsync(CommonPackets.TransmissionEnd);

            State = ClientState.WaitingForIngameAck;
            Console.WriteLine($"[CLIENT {PlayerIndex}] New character created, waiting for ack");
            return;
        }

        // Select existing character
        if (decoded[0] == 0x15)
        {
            int charIndex = decoded.Length > 17 ? decoded[17] / 4 - 1 : 0;
            Console.WriteLine($"[CLIENT {PlayerIndex}] Select slot {charIndex}");

            var characters = _server.Database.GetCharacters(Player!.CharacterIds);
            if (charIndex >= 0 && charIndex < characters.Count)
            {
                ActiveCharacter = characters[charIndex];
            }
            else if (characters.Count > 0)
            {
                ActiveCharacter = characters[0];
            }
            else
            {
                // No characters — shouldn't happen, but create default
                ActiveCharacter = _server.Database.CreateCharacter("Wanderer", Player.Id);
                ActiveCharacter.X = 2614; ActiveCharacter.Y = 157.8f; ActiveCharacter.Z = 1293;
                _server.Database.SaveCharacter(ActiveCharacter);
            }

            var gameData = CharacterSerializer.ToGameDataBytes(ActiveCharacter, PlayerIndex);
            await SendAsync(gameData);
            await SendAsync(CommonPackets.TransmissionEnd);

            State = ClientState.WaitingForIngameAck;
            Console.WriteLine($"[CLIENT {PlayerIndex}] Character [{ActiveCharacter.Name}] selected");
            return;
        }

        Console.WriteLine($"[CLIENT {PlayerIndex}] Unknown charselect action: 0x{decoded[0]:X2}");
    }

    // ===== STEP 5: Ingame ACK -> enter world =====
    private async Task HandleIngameAck(byte[] rawData)
    {
        if (rawData[0] < 0x13)
        {
            // Not the ACK we're waiting for, ignore
            return;
        }

        Console.WriteLine($"[CLIENT {PlayerIndex}] Ingame ACK -> entering world");

        // Send NewCharacterWorldData
        var worldData = CommonPackets.NewCharacterWorldData(PlayerIndex);
        await SendAsync(worldData);

        // Send two hardcoded world state packets (NPC/object data)
        await SendAsync(BuildWorldStatePacket1());
        await SendAsync(BuildWorldStatePacket2());

        await Task.Delay(50);
        await SendAsync(CommonPackets.TransmissionEnd);

        State = ClientState.InGame;
        _lastPing3s = _lastPing6s = _lastPing15s = DateTime.UtcNow;

        Console.WriteLine($"[CLIENT {PlayerIndex}] === IN GAME === [{ActiveCharacter?.Name}] at ({ActiveCharacter?.X:F0}, {ActiveCharacter?.Y:F0}, {ActiveCharacter?.Z:F0})");

        // Notify other players about this new player
        await BroadcastSpawnToOthers();
    }

    // ===== IN-GAME PACKET HANDLER =====
    private async Task HandleIngamePacket(byte[] rawData)
    {
        if (rawData.Length < 2) return;

        var decoded = PacketCodec.DecodeClientPacket(rawData);

        switch (decoded[0])
        {
            case 0x26: // Ping/movement (38 bytes)
                await HandlePingMovement(decoded);
                break;

            case 0x1A: // Chat or pickup
                if (decoded.Length > 15 && decoded[13] == 0x08 && decoded[14] == 0x40 && decoded[15] == 0x43)
                {
                    Console.WriteLine($"[CLIENT {PlayerIndex}] Chat message (not yet implemented)");
                }
                break;

            case 0x13: // Group actions
                Console.WriteLine($"[CLIENT {PlayerIndex}] Group action");
                break;

            default:
                // Log unknown packets for debugging
                if (decoded.Length > 4)
                    Console.WriteLine($"[CLIENT {PlayerIndex}] Packet 0x{decoded[0]:X2} ({decoded.Length}b)");
                break;
        }
    }

    // ===== MOVEMENT & PING =====
    private async Task HandlePingMovement(byte[] decoded)
    {
        if (decoded.Length < 38 || ActiveCharacter == null) return;

        // Extract coordinates from ping
        try
        {
            var coords = CoordsHelper.GetCoordsFromPingBytes(decoded);
            ActiveCharacter.X = (float)coords.X;
            ActiveCharacter.Y = (float)-coords.Y; // Y inverted
            ActiveCharacter.Z = (float)-coords.Z; // Z inverted

            // Send pong response
            await SendPong(decoded);

            // Broadcast movement to other players
            await BroadcastMovement(coords);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT {PlayerIndex}] Coords parse error: {ex.Message}");
        }
    }

    private async Task SendPong(byte[] pingData)
    {
        if (pingData.Length < 30) return;

        // Build pong: take bytes [9..30] from ping, XOR byte[5], add counter
        var pongData = new byte[13];
        Array.Copy(pingData, 9, pongData, 0, Math.Min(21, pingData.Length - 9));

        // XOR top bit alternating
        if (_pingShouldXorTopBit)
            pongData[5] ^= 0x80;
        _pingShouldXorTopBit = !_pingShouldXorTopBit;

        // Add counter
        pongData[11] = ByteHelper.MinorByte(_pingCounter);
        pongData[12] = ByteHelper.MajorByte(_pingCounter);
        _pingCounter++;

        // Wrap as packet
        var packet = PacketBuilder.Build(pongData, padZeros: 1);
        await SendAsync(packet);
    }

    private async Task BroadcastMovement(WorldCoords coords)
    {
        if (ActiveCharacter == null) return;

        var movePacket = CommonPackets.BuildMoveObjectPacket(
            coords.X, coords.Y, coords.Z, coords.Turn, PlayerIndex);

        foreach (var client in _server.GetIngameClients())
        {
            if (client.PlayerIndex != PlayerIndex)
            {
                await client.SendAsync(movePacket);
            }
        }
    }

    private async Task BroadcastSpawnToOthers()
    {
        // For now, just log. Full entity spawn requires PacketPart templates.
        Console.WriteLine($"[CLIENT {PlayerIndex}] Spawn broadcast (simplified)");

        // Despawn notification will be sent on disconnect
    }

    // ===== KEEPALIVE =====
    private async Task SendKeepaliveIfNeeded()
    {
        if (State != ClientState.InGame) return;

        var now = DateTime.UtcNow;

        if ((now - _lastPing3s).TotalSeconds >= 3)
        {
            await SendAsync(CommonPackets.TransmissionEnd);
            _lastPing3s = now;
        }

        if ((now - _lastPing6s).TotalSeconds >= 6)
        {
            await SendAsync(CommonPackets.SixSecondPing(PlayerIndex));
            _lastPing6s = now;
        }

        if ((now - _lastPing15s).TotalSeconds >= 15)
        {
            await SendAsync(CommonPackets.FifteenSecondPing(PlayerIndex));
            _lastPing15s = now;
        }
    }

    // ===== CHARACTER NAME DECODE =====
    private static string DecodeCharacterName(byte[] decoded)
    {
        // Name is encoded with 5-bit shift starting after header bytes
        // This is a simplified decoder — extract printable bytes from the packet
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var win1251 = System.Text.Encoding.GetEncoding(1251);

            // Name data starts around byte 17 with bit packing
            var nameBytes = new List<byte>();
            int start = 17;
            for (int i = start; i < decoded.Length - 1; i++)
            {
                byte b = (byte)(((decoded[i] & 0b11111000) >> 3) + ((decoded[i + 1] & 0b111) << 5));
                if (b == 0) break;
                nameBytes.Add(b);
            }

            return nameBytes.Count > 0 ? win1251.GetString(nameBytes.ToArray()) : "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    // ===== WORLD STATE PACKETS =====
    // Hardcoded from SphereEmu IngameAckHandler — world/NPC initialization data
    private byte[] BuildWorldStatePacket1()
    {
        var id = PlayerIndex;
        return
        [
            0xBA, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04,
            ByteHelper.MajorByte(id), ByteHelper.MinorByte(id),
            0x08, 0x40, 0x20, 0xCE, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];
    }

    private byte[] BuildWorldStatePacket2()
    {
        var id = PlayerIndex;
        return
        [
            0x83, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04,
            ByteHelper.MajorByte(id), ByteHelper.MinorByte(id),
            0x08, 0x40, 0x20, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];
    }

    // ===== SEND / DISCONNECT =====
    public async Task SendAsync(byte[] data)
    {
        try
        {
            if (_tcp.Connected)
                await _stream.WriteAsync(data, _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT {PlayerIndex}] Send error: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        if (_cts.IsCancellationRequested) return;

        Console.WriteLine($"[CLIENT {PlayerIndex}] Disconnecting [{ActiveCharacter?.Name}]");

        // Save character state
        if (ActiveCharacter != null)
            _server.Database.SaveCharacter(ActiveCharacter);

        // Notify others about despawn
        if (State == ClientState.InGame)
        {
            var despawn = CommonPackets.DespawnEntity(PlayerIndex);
            foreach (var client in _server.GetIngameClients())
            {
                if (client.PlayerIndex != PlayerIndex)
                    _ = client.SendAsync(despawn);
            }
        }

        _cts.Cancel();
        _stream.Dispose();
        _tcp.Dispose();
        _server.RemoveClient(this);
    }
}

public enum ClientState
{
    Connected,
    WaitingForInitialData,
    WaitingForLogin,
    WaitingForCharacterSelect,
    WaitingForIngameAck,
    InGame
}

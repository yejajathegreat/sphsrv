using System.Net.Sockets;
using System.Text;
using SphereServer.Database;
using SphereServer.Helpers;
using SphereServer.Protocol;

namespace SphereServer.Network;

/// <summary>
/// Handles one connected client. Full flow ported 1:1 from knelse Server.HandleClientAsync:
/// TCP accept -> handshake -> login -> character select -> enter game -> ingame loop.
/// </summary>
public class ClientSession
{
    private const int BUFSIZE = 1024;

    public ushort PlayerIndex { get; }
    public CharacterRecord? CurrentCharacter { get; set; }

    private readonly TcpClient _client;
    private readonly NetworkStream _ns;
    private readonly SphereGameServer _server;

    // Ping state (ported from knelse ClientData)
    private ushort _pingCounter;
    private bool _pingShouldXorTopBit;
    private string? _pingPreviousClientPingString;

    public ClientSession(TcpClient client, ushort playerIndex, SphereGameServer server)
    {
        _client = client;
        PlayerIndex = playerIndex;
        _server = server;
        _ns = client.GetStream();
    }

    public async Task HandleClientAsync()
    {
        try
        {
            await Task.Yield();

            var playerIndexStr = Convert.ToHexString(new[]
            {
                ByteHelper.GetSecondByte(PlayerIndex),
                ByteHelper.GetFirstByte(PlayerIndex)
            });

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Handling client " + playerIndexStr);
            Console.ForegroundColor = ConsoleColor.White;

            // Step 1: Send ReadyToLoadInitialData
            Console.WriteLine("SRV: Ready to load initial data");
            await _ns.WriteAsync(CommonPackets.ReadyToLoadInitialData);

            var rcvBuffer = new byte[BUFSIZE];

            // Step 2: Wait for connection initialized
            while (await _ns.ReadAsync(rcvBuffer) == 0) { }
            Console.WriteLine("CLI: Connection initialized");

            // Step 3: Send server credentials
            await _ns.WriteAsync(CommonPackets.ServerCredentials(PlayerIndex));
            Console.WriteLine("SRV: Credentials sent");

            // Step 4: Wait for login data (> 12 bytes)
            while (await _ns.ReadAsync(rcvBuffer) <= 12) { }
            Console.WriteLine("CLI: Login data sent");

            // Step 5: Decode login
            var (login, password) = LoginDecoder.Decode(rcvBuffer);

            // Step 6: Check login & get characters
            var player = _server.Database.Login(login, password);
            if (player == null)
            {
                await _ns.WriteAsync(CommonPackets.AccountAlreadyInUse(PlayerIndex));
                CloseConnection();
                return;
            }

            // Get character list data (up to 3 slots)
            var characters = _server.Database.GetCharacters(player.CharacterIds);

            // Step 7: Send character select start data
            await _ns.WriteAsync(CommonPackets.CharacterSelectStartData(PlayerIndex));
            Console.WriteLine("SRV: Character select screen data - initial");
            Thread.Sleep(50);

            // Step 8: Send character list (3 slots)
            var charListBytes = BuildCharacterListBytes(characters, PlayerIndex);
            await _ns.WriteAsync(charListBytes);
            Console.WriteLine("SRV: Character select screen data - player characters");
            Thread.Sleep(50);

            // Step 9: Start 15-second ping thread
            CreateFifteenSecondPingThread();

            // Step 10: Start transmission end ping thread
            CreateTransmissionEndPacketPingThread();

            // Step 11: Character screen create/delete/select loop
            var selectedCharacterIndex = await CharacterScreenCreateDeleteSelectAsync(
                rcvBuffer, characters, player);

            if (selectedCharacterIndex == -1)
            {
                selectedCharacterIndex = rcvBuffer[17] / 4 - 1;
            }

            // Refresh player from DB (CharacterIds may have been updated during creation)
            var freshPlayer = _server.Database.GetPlayerById(player.Id);
            if (freshPlayer != null)
                player = freshPlayer;
            characters = _server.Database.GetCharacters(player.CharacterIds);
            CharacterRecord? selectedCharacter = null;
            if (selectedCharacterIndex >= 0 && selectedCharacterIndex < characters.Count)
                selectedCharacter = characters[selectedCharacterIndex];
            else if (characters.Count > 0)
                selectedCharacter = characters[0];

            if (selectedCharacter == null)
            {
                Console.WriteLine("SRV: No character found, closing connection");
                CloseConnection();
                return;
            }

            CurrentCharacter = selectedCharacter;

            // Step 12: Send game data
            Console.WriteLine("CLI: Enter game");
            await _ns.WriteAsync(CharacterSerializer.ToGameDataBytes(selectedCharacter, PlayerIndex));
            _server.IncrementPlayerCount();

            // Step 13: Wait for 0x13 ACK
            while (await _ns.ReadAsync(rcvBuffer) != 0x13) { }

            // Step 14: Send world data
            await WorldDataTest.SendNewCharacterWorldData(_ns, playerIndexStr);

            // Step 15: Teleport to new player dungeon
            MoveToNewPlayerDungeon(selectedCharacter, playerIndexStr);

            // Step 16: Start 6-second ping thread
            CreateSixSecondPingThread();

            // Step 17: Start test mob data thread
#pragma warning disable CS4014
            Task.Run(async () =>
            {
                while (_ns.CanWrite)
                {
                    await _ns.WriteAsync(TestHelper.GetTestMobData());
                    Thread.Sleep(1000);
                }
            });
#pragma warning restore CS4014

            Thread.Sleep(50);

            // Step 18: Main ingame loop
            while (_ns.CanRead)
            {
                var length = await _ns.ReadAsync(rcvBuffer);
                if (length == 0) continue;

                switch (rcvBuffer[0])
                {
                    case 0x26: // ping
                        await SendPingResponse(rcvBuffer);
                        break;
                    case 0x08: // echo
                        await _ns.WriteAsync(CommonPackets.Echo(PlayerIndex));
                        break;
                    default:
                        continue;
                }
            }

            CloseConnection();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            CloseConnection();
        }
    }

    /// <summary>
    /// Character select/create/delete loop.
    /// Ported 1:1 from knelse Server.CharacterScreenCreateDeleteSelectAsync.
    /// </summary>
    private async Task<int> CharacterScreenCreateDeleteSelectAsync(
        byte[] rcvBuffer, List<CharacterRecord> characters, PlayerRecord player)
    {
        while (await _ns.ReadAsync(rcvBuffer) != 0x15)
        {
            // Delete character
            if (rcvBuffer[0] == 0x2A)
            {
                var charIndex = rcvBuffer[17] / 4 - 1;
                if (charIndex >= 0 && charIndex < characters.Count)
                {
                    Console.WriteLine($"Delete character [{charIndex}] - [{characters[charIndex].Name}]");
                    _server.Database.DeleteCharacter(characters[charIndex].Id, player.Id);
                }
                CloseConnection();
                return -1;
            }

            if (rcvBuffer[0] < 0x1B)
                continue;

            // Create new character - decode name
            var len = rcvBuffer[0] - 20 - 5;
            var charDataBytesStart = rcvBuffer[0] - 5;
            var nameCheckBytes = rcvBuffer[20..];
            var charDataBytes = rcvBuffer[charDataBytesStart..rcvBuffer[0]];
            var sb = new StringBuilder();
            var firstLetterCharCode = (((nameCheckBytes[1] & 0b11111) << 3) + (nameCheckBytes[0] >> 5));
            var firstLetterShouldBeRussian = false;

            for (var i = 1; i < len; i++)
            {
                var currentCharCode = (((nameCheckBytes[i] & 0b11111) << 3) + (nameCheckBytes[i - 1] >> 5));

                if (currentCharCode % 2 == 0)
                {
                    var currentLetter = (char)(currentCharCode / 2);
                    sb.Append(currentLetter);
                }
                else
                {
                    var currentLetter = currentCharCode >= 193
                        ? (char)((currentCharCode - 192) / 2 + '\u0430') // Russian lowercase
                        : (char)((currentCharCode - 129) / 2 + '\u0410'); // Russian uppercase
                    sb.Append(currentLetter);

                    if (i == 2)
                        firstLetterShouldBeRussian = true;
                }
            }

            string name;
            if (firstLetterShouldBeRussian)
            {
                firstLetterCharCode += 1;
                var firstLetter = firstLetterCharCode >= 193
                    ? (char)((firstLetterCharCode - 192) / 2 + '\u0430')
                    : (char)((firstLetterCharCode - 129) / 2 + '\u0410');
                name = firstLetter + sb.ToString()[1..];
            }
            else
            {
                name = sb.ToString();
            }

            var isNameValid = !_server.Database.IsNameTaken(name);
            Console.WriteLine(isNameValid ? $"SRV: Name [{name}] OK" : $"SRV: Name [{name}] already exists!");

            if (!isNameValid)
            {
                await _ns.WriteAsync(CommonPackets.NameAlreadyExists(PlayerIndex));
            }
            else
            {
                var isGenderFemale = (charDataBytes[1] >> 4) % 2 == 1;
                var faceType = ((charDataBytes[1] & 0b111111) << 2) + (charDataBytes[0] >> 6);
                var hairStyle = ((charDataBytes[2] & 0b111111) << 2) + (charDataBytes[1] >> 6);
                var hairColor = ((charDataBytes[3] & 0b111111) << 2) + (charDataBytes[2] >> 6);
                var tattoo = ((charDataBytes[4] & 0b111111) << 2) + (charDataBytes[3] >> 6);

                if (isGenderFemale)
                {
                    faceType = 256 - faceType;
                    hairStyle = 255 - hairStyle;
                    hairColor = 255 - hairColor;
                    tattoo = 255 - tattoo;
                }

                var charIndex = (rcvBuffer[17] / 4 - 1);

                var newChar = _server.Database.CreateCharacter(
                    name, player.Id, charIndex,
                    isGenderFemale, (byte)faceType, (byte)hairStyle, (byte)hairColor, (byte)tattoo);

                // Refresh characters list
                characters = _server.Database.GetCharacters(player.CharacterIds);

                await _ns.WriteAsync(CommonPackets.NameCheckPassed(PlayerIndex));
                return charIndex;
            }
        }

        return -1;
    }

    /// <summary>
    /// Ping response, ported 1:1 from knelse ClientData.SendPingResponse.
    /// </summary>
    private async Task SendPingResponse(byte[] rcvBuffer)
    {
        var clientPingBytesForComparison = rcvBuffer[17..38];
        var clientPingBytesForPong = rcvBuffer[9..21];
        var clientPingBinaryStr =
            ByteHelper.ByteArrayToBinaryString(clientPingBytesForComparison, false, true);

        if (string.IsNullOrEmpty(_pingPreviousClientPingString))
        {
            _pingPreviousClientPingString = clientPingBinaryStr;
        }
        else
        {
            var pingHasChanges = string.Compare(clientPingBinaryStr, _pingPreviousClientPingString,
                StringComparison.Ordinal);

            if (pingHasChanges != 0)
            {
                var coords = CoordsHelper.GetCoordsFromPingBytes(rcvBuffer);
                if (CurrentCharacter != null)
                {
                    CurrentCharacter.X = coords.X;
                    CurrentCharacter.Y = coords.Y;
                    CurrentCharacter.Z = coords.Z;
                    CurrentCharacter.Turn = coords.Turn;
                }
                Console.WriteLine(coords.ToDebugString());
                _pingPreviousClientPingString = clientPingBinaryStr;
            }
        }

        var topByteToXor = clientPingBytesForPong[5];
        if (_pingShouldXorTopBit)
            topByteToXor ^= 0b10000000;

        if (_pingCounter == 0)
        {
            var first = (ushort)((clientPingBytesForPong[7] << 8) + clientPingBytesForPong[6]);
            first -= 0xE001;
            _pingCounter = (ushort)(0xE001 + first / 12);
        }

        var pong = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, topByteToXor, ByteHelper.GetFirstByte(_pingCounter),
            ByteHelper.GetSecondByte(_pingCounter), 0x00, 0x00, 0x00, 0x00, 0x00
        };

        Array.Copy(clientPingBytesForPong, pong, 5);
        Array.Copy(clientPingBytesForPong, 8, pong, 8, 4);
        await _ns.WriteAsync(PacketBuilder.Build(pong, 1));
        _pingShouldXorTopBit = !_pingShouldXorTopBit;
        _pingCounter++;

        // overflow
        if (_pingCounter < 0xE001)
            _pingCounter = 0xE001;
    }

    /// <summary>
    /// Teleport to new player dungeon, ported from knelse Server.MoveToNewPlayerDungeon.
    /// </summary>
    private void MoveToNewPlayerDungeon(CharacterRecord selectedCharacter, string playerIndexStr)
    {
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            Thread.Sleep(3000);

            var newDungeonCoords = new WorldCoords(-1098, 4501.62158203125, 1900, 1.55);
            var teleportCoords = new WorldCoords(-1098.69506835937500, 4501.61474609375000,
                1900.05493164062500, 1.57079637050629);

            await _ns.WriteAsync(CharacterSerializer.GetTeleportPacket(
                teleportCoords, PlayerIndex, playerIndexStr));
            Thread.Sleep(100);

            // Enter new game instance data
            var enterNewGame_6 =
                "BF002C0100067A2C0C10802F811F010BE2E00320A14B02000000000000000050649101A000039AFE00850900F8F9AF00063E00C044620050C62200C0762200441619000A2F809DF50B60E1337CA2838DC436A88C45F0FBF144C1882C3200145E4020A0F00CA2838DC436A88C45F0FBF144856FD0ED2CFE558F4806568F480606F8212C400D3E9F1045629756C62241A476A2E550140014433A29DE0785170000F8252CA01F3E19A14662B053C62249C2762280441619890A5900F0FFFFFF0F" +
                "C1002C0100067A150B2483CF38A391B89495B1C813319E680C140500C550EA8000C0CF62813FF001CD2512ABA232160995CB130124B2C80050C80280FFFFFFFFC2132C1C0004FC0C5800161F00095D12000000000000000080228B0C000518D0F407286401C0FFFFFFFFEF7F85CFF001002612038032160100B6130120B2C8005078810081C233646DCF15EB822612257CD01517BE01000000000000000000000000A0F00C19D976C5DBB489440D7E72C5856F0000000000000000000000000000" +
                "C8002C0100067AFF2B7C46E11908C0E68AF5411389FE18E28A0BDF00000000000000000000000000F00360E1337C008089C400A08C450080ED4400882C3200145E4040A0F00C737F75C514A18944208271C5856F00000000000000000000000000F8E53EF0193E00C044620050C62200C0762200441619000A2F30205078069906BAE283DA44A22BC1B9E2C23700000000000000000000000000FC741FF8019FB95C2231DE2A6311FBDE3C1100228B0C003F061690C027C396489CBFC958E4DBD74E0480C8220300" +
                "C9002C0100067A0C2C2041E10502007E022C2081CF1B9A91D8F292B1080EB09D080091450680C20B0800FC065840029F58C72331AE2863911E863B1100228B0C0085171800F8093F80043E1FD34662154FC622EEA8782200441619000A2F4000F02B5300097CE8AF8BC446968C45D242F14400882C3200145EA000E0A7BF0212F838E52512B36132164182C6130120B2C80050780103809FDE02AEE10320A14B02000000000000000050649101A000039AFE00850900F8F9AF00063E00C044620050C62200C0762200441619000A2F809DF50B60E1337CA2838DC436A88C45F0FBF144856FD0ED2CFE558F4806568F480606F8212C400D3E9F1045629756C62241A476A2E550140014433A29DE0785170000F8252CA01F3E19A14662B053C62249C2762280441619890A5900F0FFFFFF0F" +
                "1B002C0100067A7A0BB846010634FD010A131050C80280FFFFFF7F" +
                "2D002C01006DF78A2CDBE1400F61016A1098F9F435FEF22F6101FD10006DFED71FC0CF62813F10547EFED90900";

            await _ns.WriteAsync(Convert.FromHexString(enterNewGame_6));
            Console.WriteLine($"SRV: Teleported client [{ByteHelper.GetFirstByte(PlayerIndex) * 256 + ByteHelper.GetSecondByte(PlayerIndex)}] to default new player dungeon");

            await _ns.WriteAsync(TestHelper.GetNewPlayerDungeonMobData(newDungeonCoords));
        });
#pragma warning restore CS4014
    }

    private void CreateTransmissionEndPacketPingThread()
    {
        Thread.Sleep(100);
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            while (_ns.CanWrite)
            {
                await _ns.WriteAsync(CommonPackets.TransmissionEndPacket);
                Thread.Sleep(3000);
            }
        });
#pragma warning restore CS4014
    }

    private void CreateFifteenSecondPingThread()
    {
        Thread.Sleep(200);
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            while (_ns.CanWrite)
            {
                await _ns.WriteAsync(CommonPackets.FifteenSecondPing(PlayerIndex));
                Thread.Sleep(15000);
            }
        });
#pragma warning restore CS4014
    }

    private void CreateSixSecondPingThread()
    {
        Thread.Sleep(300);
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            while (_ns.CanWrite)
            {
                await _ns.WriteAsync(CommonPackets.SixSecondPing(PlayerIndex));
                Thread.Sleep(6000);
            }
        });
#pragma warning restore CS4014
    }

    private void CloseConnection()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Client disconnect...");
        Console.ForegroundColor = ConsoleColor.White;

        _server.DecrementPlayerCount();

        if (CurrentCharacter != null)
            _server.Database.SaveCharacter(CurrentCharacter);

        try { _ns.Close(); } catch { }
        try { _client.Close(); } catch { }
    }

    /// <summary>
    /// Build 3-slot character list (324 bytes total = 3 x 108).
    /// </summary>
    private static byte[] BuildCharacterListBytes(List<CharacterRecord> characters, ushort playerIndex)
    {
        var charDataBytes = new byte[324];

        var first = characters.Count > 0
            ? CharacterSerializer.ToCharacterListBytes(characters[0], playerIndex)
            : CommonPackets.CreateNewCharacterData(playerIndex);
        var second = characters.Count > 1
            ? CharacterSerializer.ToCharacterListBytes(characters[1], playerIndex)
            : CommonPackets.CreateNewCharacterData(playerIndex);
        var third = characters.Count > 2
            ? CharacterSerializer.ToCharacterListBytes(characters[2], playerIndex)
            : CommonPackets.CreateNewCharacterData(playerIndex);

        Array.Copy(first, 0, charDataBytes, 0, 108);
        Array.Copy(second, 0, charDataBytes, 108, 108);
        Array.Copy(third, 0, charDataBytes, 216, 108);

        return charDataBytes;
    }
}

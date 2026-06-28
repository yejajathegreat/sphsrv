using System.Net.Sockets;

namespace SphereServer.Protocol;

/// <summary>
/// World data sent when a new character enters the game.
/// Ported from knelse CommonPackets.NewCharacterWorldData() + IngameAckHandler.
///
/// Only the first 168-byte packet is sent. The subsequent 0x72 packets are
/// file consistency checks that knelse explicitly removed:
/// "this is actually a query to check file consistency on client and not required"
/// </summary>
public static class WorldDataTest
{
    public static async Task SendNewCharacterWorldData(NetworkStream ns, string playerIndexStr)
    {
        // Packet 1: NewCharacterWorldData[0] — 168 bytes (0xA8)
        // This is the main world initialization packet
        await ns.WriteAsync(Convert.FromHexString(
            $"A8002c01000004{playerIndexStr}08406102000A82A0C3E10C000000005010849D0F6700A0252680107D380300142460A720081420CE00640000000D1C0205000080C64640406383ACCCCCAC6C8C6E8E8B0BC0A9EC0E640C2D4C0E24CD0DE42CACAD4C0764C9AD8C0D2E6C2C0C0465C9AD8C0D2E6C2C0C0425A646C565AECC0CE02B2625C54501C08908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04"));

        Thread.Sleep(50);

        // Packet 2: Zone/NPC initialization data (BA00...) from knelse IngameAckHandler
        // Note: uses 000000{id} prefix, not 000004{id}
        await ns.WriteAsync(Convert.FromHexString(
            $"BA002C01000000{playerIndexStr}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603848F1535B10F2B6391702035D1F643F24F411072A0D901900100000A5290530F0000D0001170AA2A48410E32000000"));

        // Packet 3: Additional zone data (8300...) from knelse IngameAckHandler
        await ns.WriteAsync(Convert.FromHexString(
            $"83002C01000000{playerIndexStr}08406102000A824011820E400600005010841C000000000000808220E888A00300000000140461A70B1D0068890920445DE8000005419820480768010000280802402D3B007D0000404110706CD901060000000A120050908089820450142400A720013B0541A0C041072003000068E010280000003436020200"));
    }
}

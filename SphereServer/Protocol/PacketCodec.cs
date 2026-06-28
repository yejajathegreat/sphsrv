namespace SphereServer.Protocol;

/// <summary>
/// Packet codec. knelse's emulator does NOT use XOR decoding -- client packets
/// are read raw. This file is kept for compatibility but DecodeClientPacket
/// simply returns the input unchanged.
/// </summary>
public static class PacketCodec
{
    public static byte[] DecodeClientPacket(byte[] input, int start = 0)
    {
        // knelse's emulator reads raw bytes, no XOR decoding
        return input;
    }
}

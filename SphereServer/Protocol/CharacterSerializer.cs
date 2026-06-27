using System.Text;
using SphereServer.Database;
using SphereServer.Helpers;
using static SphereServer.Helpers.ByteHelper;

namespace SphereServer.Protocol;

/// <summary>
/// Serializes character data for the Sphere protocol.
/// Two formats:
/// - ToCharacterListBytes: 108-byte slot for character select screen (3 slots)
/// - ToGameDataBytes: variable-length packet for entering the game world
///
/// Both use bitwise packing with 2-bit carry between fields.
/// Ported from SphereEmu CharacterDbEntrySerializer.cs
/// </summary>
public static class CharacterSerializer
{
    private static readonly Encoding Win1251;

    static CharacterSerializer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>
    /// 108-byte character slot for the selection screen.
    /// Stats are packed in a continuous bit stream with 2-bit carry.
    /// </summary>
    public static byte[] ToCharacterListBytes(CharacterRecord ch, ushort clientIndex)
    {
        var nameEncoded = new byte[19];
        var nameBytes = Win1251.GetBytes(ch.Name);
        Array.Copy(nameBytes, nameEncoded, Math.Min(nameBytes.Length, 19));

        ushort maxHp = (ushort)ch.MaxHp;
        ushort maxMp = (ushort)ch.MaxMp;
        ushort str = (ushort)ch.Strength;
        ushort agi = (ushort)ch.Agility;
        ushort acc = (ushort)ch.Accuracy;
        ushort end = (ushort)ch.Endurance;
        ushort earth = (ushort)ch.Earth;
        ushort air = (ushort)ch.Air;
        ushort water = (ushort)ch.Water;
        ushort fire = (ushort)ch.Fire;
        ushort pdef = 0, mdef = 0;
        byte karma = 0;
        ushort satMax = 200, satCur = 200;
        ushort curHp = (ushort)ch.Hp;
        ushort curMp = (ushort)ch.Mp;
        int titleMinusOne = Math.Max(0, ch.Level - 1);
        int degreeMinusOne = 0;
        uint titleXp = 0, degreeXp = 0;
        ushort availTitleStats = 0, availDegreeStats = 0;

        // Pack stats with 2-bit carry chain
        byte hpMax1 = (byte)(((maxHp & 0b111111) << 2) + 1);
        byte hpMax2 = (byte)((maxHp & 0b11111111000000) >> 6);
        byte mpMax1 = PackLow(maxMp, maxHp);
        byte mpMax2 = PackHigh(maxMp);
        byte str1 = PackLow(str, maxMp); byte str2 = PackHigh(str);
        byte agi1 = PackLow(agi, str); byte agi2 = PackHigh(agi);
        byte acc1 = PackLow(acc, agi); byte acc2 = PackHigh(acc);
        byte end1 = PackLow(end, acc); byte end2 = PackHigh(end);
        byte earth1 = PackLow(earth, end); byte earth2 = PackHigh(earth);
        byte air1 = PackLow(air, earth); byte air2 = PackHigh(air);
        byte water1 = PackLow(water, air); byte water2 = PackHigh(water);
        byte fire1 = PackLow(fire, water); byte fire2 = PackHigh(fire);
        byte pdef1 = PackLow(pdef, fire); byte pdef2b = PackHigh(pdef);
        byte mdef1 = PackLow(mdef, pdef); byte mdef2b = PackHigh(mdef);
        byte karma1 = (byte)(((karma & 0b111111) << 2) + ((mdef & 0b1100000000000000) >> 14));
        byte satMax1 = (byte)(((satMax & 0b111111) << 2) + ((karma & 0b11000000) >> 6)); // karma is byte, high bits 0
        byte satMax2 = (byte)((satMax & 0b11111111000000) >> 6);
        byte titleLvl1 = PackLow((ushort)titleMinusOne, satMax);
        byte titleLvl2 = PackHigh((ushort)titleMinusOne);
        byte degreeLvl1 = PackLow((ushort)degreeMinusOne, (ushort)titleMinusOne);
        byte degreeLvl2 = PackHigh((ushort)degreeMinusOne);

        // XP (32-bit each, packed with 2-bit carry)
        byte titleXp1 = (byte)(((titleXp & 0b111111) << 2) + (((ushort)degreeMinusOne & 0b1100000000000000) >> 14));
        byte titleXp2 = (byte)((titleXp & 0b11111111000000) >> 6);
        byte titleXp3 = (byte)((titleXp & 0b1111111100000000000000) >> 14);
        byte titleXp4 = (byte)((titleXp & 0b111111110000000000000000000000) >> 22);
        byte degreeXp1 = (byte)(((degreeXp & 0b111111) << 2) + ((titleXp & 0b11000000000000000000000000000000) >> 30));
        byte degreeXp2 = (byte)((degreeXp & 0b11111111000000) >> 6);
        byte degreeXp3 = (byte)((degreeXp & 0b1111111100000000000000) >> 14);
        byte degreeXp4 = (byte)((degreeXp & 0b111111110000000000000000000000) >> 22);
        byte satCur1 = (byte)(((satCur & 0b111111) << 2) + ((degreeXp & 0b11000000000000000000000000000000) >> 30));
        byte satCur2 = (byte)((satCur & 0b11111111000000) >> 6);
        byte hpCur1 = PackLow(curHp, satCur); byte hpCur2 = PackHigh(curHp);
        byte mpCur1 = PackLow(curMp, curHp); byte mpCur2 = PackHigh(curMp);
        byte titleStats1 = PackLow(availTitleStats, curMp);
        byte titleStats2 = PackHigh(availTitleStats);
        byte degreeStats1 = PackLow(availDegreeStats, availTitleStats);
        byte degreeStats2 = PackHigh(availDegreeStats);
        byte degreeStats3 = (byte)((0b111010 << 2) + ((availDegreeStats & 0b1100000000000000) >> 14));

        byte isFemale1 = (byte)((ch.IsFemale ? 1 : 0) << 2);

        // Name with 2-bit carry
        var nameOut = new byte[19];
        nameOut[0] = (byte)((nameEncoded[0] & 0b111111) << 2);
        for (int i = 1; i < 19; i++)
            nameOut[i] = (byte)(((nameEncoded[i] & 0b111111) << 2) + ((nameEncoded[i - 1] & 0b11000000) >> 6));

        byte face1 = (byte)(((0 & 0b111111) << 2) + ((nameEncoded[18] & 0b11000000) >> 6));
        byte hair1 = 0, hairColor1 = 0, tattoo1 = 0;
        byte boots = 0, pants = 0, armor = 0, helmet = 0;
        byte gloves1 = 0, gloves2 = 0;
        byte isNotDeleted = (byte)((1 << 1) + 1); // true

        return
        [
            0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(clientIndex), MinorByte(clientIndex), 0x08, 0x40,
            0x60, 0x79, // lookType
            hpMax1, hpMax2, mpMax1, mpMax2,
            str1, str2, agi1, agi2, acc1, acc2, end1, end2,
            earth1, earth2, air1, air2, water1, water2, fire1, fire2,
            pdef1, pdef2b, mdef1, mdef2b,
            karma1, satMax1, satMax2,
            titleLvl1, titleLvl2, degreeLvl1, degreeLvl2,
            titleXp1, titleXp2, titleXp3, titleXp4,
            degreeXp1, degreeXp2, degreeXp3, degreeXp4,
            satCur1, satCur2, hpCur1, hpCur2, mpCur1, mpCur2,
            titleStats1, titleStats2, degreeStats1, degreeStats2, degreeStats3,
            0xC0, 0xC8, 0xC8,
            isFemale1,
            nameOut[0], nameOut[1], nameOut[2], nameOut[3], nameOut[4],
            nameOut[5], nameOut[6], nameOut[7], nameOut[8], nameOut[9],
            nameOut[10], nameOut[11], nameOut[12], nameOut[13], nameOut[14],
            nameOut[15], nameOut[16], nameOut[17], nameOut[18],
            face1, hair1, hairColor1, tattoo1,
            boots, pants, armor, helmet, gloves1, gloves2,
            0xC0, 0xC0, 0x00, 0xFC, 0xFF, 0xFF, 0xFF,
            isNotDeleted, 0x00, 0x00, 0x00, 0x00
        ];
    }

    /// <summary>
    /// Variable-length game data packet sent when player enters the world.
    /// Contains: name, clan, coordinates, equipment slots, stats, money.
    /// </summary>
    public static byte[] ToGameDataBytes(CharacterRecord ch, ushort clientIndex)
    {
        var nameEncoded = Win1251.GetBytes(ch.Name);
        var x = CoordsHelper.EncodeServerCoordinate(ch.X);
        var y = CoordsHelper.EncodeServerCoordinate(-ch.Y); // Y inverted!
        var z = CoordsHelper.EncodeServerCoordinate(-ch.Z); // Z inverted!
        var t = CoordsHelper.EncodeServerCoordinate(0); // angle

        int nameLen = nameEncoded.Length + 1;

        var data = new List<byte>
        {
            0x00, // placeholder for length, set at end
            0x01, 0x2C, 0x01, 0x00, 0x00, 0x04,
            MajorByte(clientIndex), MinorByte(clientIndex),
            0x08, 0x00,
            (byte)(((nameLen & 0b111) << 5) + 2),
            (byte)(((nameEncoded[0] & 0b111) << 5) + ((nameLen & 0b11111000) >> 3))
        };

        // Name with 5-bit shift
        for (int i = 1; i < nameEncoded.Length; i++)
            data.Add((byte)(((nameEncoded[i] & 0b111) << 5) + ((nameEncoded[i - 1] & 0b11111000) >> 3)));
        data.Add((byte)((nameEncoded[^1] & 0b11111000) >> 3));

        // No clan
        data.Add(0x00);
        data.Add(0x6E);

        // Coordinates
        data.Add(0x1A); data.Add(0x98); data.Add(0x18); data.Add(0x19);
        data.AddRange(x);
        data.AddRange(y);
        data.AddRange(z);
        data.AddRange(t);
        data.Add(0x37); data.Add(0x0D); data.Add(0x79); data.Add(0x00); data.Add(0xF0);

        // Equipment slots (all empty for new character): 32 slots x 2 bytes
        // Helmet, Amulet, Shield, Chestplate, Gloves, Belt, BraceletL, BraceletR,
        // Ring1-4, Pants, Boots, Guild, MapBook, RecipeBook, MantraBook, 4 empty,
        // Inkpot, Money, Backpack, Key1, Key2, Mission, Inventory1-10
        for (int i = 0; i < 32; i++)
        {
            data.Add(0x00); data.Add(0x00);
        }

        // 21 zero bytes padding
        for (int i = 0; i < 21; i++) data.Add(0x00);

        // Special slots (9) + Ammo + SpeedhackMantra + 6 zeroes
        for (int i = 0; i < 11; i++) { data.Add(0x00); data.Add(0x00); }
        for (int i = 0; i < 6; i++) data.Add(0x00);
        data.Add(0xF0);

        // 150 zero bytes (reserved)
        for (int i = 0; i < 150; i++) data.Add(0x00);

        // Stats block
        ushort curHp = (ushort)ch.Hp;
        ushort maxHp = (ushort)ch.MaxHp;
        byte karma = 0;
        int degreeMinusOne = 0;
        int titleMinusOne = Math.Max(0, ch.Level - 1);
        int toEncode = degreeMinusOne * 100 + titleMinusOne;
        int money = (int)ch.Money;

        data.Add((byte)(((curHp & 0b111) << 5) + 0b10011));
        data.Add((byte)((curHp & 0b11111111000) >> 3));
        data.Add((byte)(((maxHp & 0b11) << 6) + (0b100 << 3) + ((curHp & 0b11100000000000) >> 11)));
        data.Add((byte)((maxHp & 0b1111111100) >> 2));
        data.Add((byte)((karma << 4) + ((maxHp & 0b11110000000000) >> 10)));
        data.Add((byte)(((toEncode & 0b111111) << 2) + 2));
        data.Add((byte)((toEncode & 0b11111111000000) >> 6));
        data.Add(0x80); // separator
        data.Add(0x00); // guild = none
        data.Add((byte)(((money & 0b1111) << 4) + 0)); // guildLevelMinusOne = 0
        data.Add((byte)((money & 0b111111110000) >> 4));
        data.Add((byte)((money & 0b11111111000000000000) >> 12));
        data.Add((byte)((money & 0b1111111100000000000000000000) >> 20));
        data.Add((byte)((money & 0b11110000000000000000000000000000u) >> 28));

        var arr = data.ToArray();
        arr[0] = (byte)arr.Length;
        return arr;
    }

    // Helper: pack lower 6 bits of value with carry from previous field's upper 2 bits
    private static byte PackLow(ushort value, ushort prev) =>
        (byte)(((value & 0b111111) << 2) + ((prev & 0b1100000000000000) >> 14));

    private static byte PackHigh(ushort value) =>
        (byte)((value & 0b11111111000000) >> 6);
}

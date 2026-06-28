using System.Text;
using SphereServer.Database;
using SphereServer.Helpers;
using static SphereServer.Helpers.ByteHelper;

namespace SphereServer.Protocol;

/// <summary>
/// Serializes character data for the Sphere protocol.
/// Ported 1:1 from knelse CharacterData.cs (ToCharacterListByteArray, ToGameDataByteArray, GetTeleportAndUpdateCharacterByteArray).
/// </summary>
public static class CharacterSerializer
{
    private static readonly Encoding Win1251;

    static CharacterSerializer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
    }

    public static Encoding GetWin1251() => Win1251;

    /// <summary>
    /// 108-byte character slot for the selection screen.
    /// Ported 1:1 from knelse CharacterData.ToCharacterListByteArray()
    /// </summary>
    public static byte[] ToCharacterListBytes(CharacterRecord ch, ushort playerIndex)
    {
        var nameEncodedWithPadding = new byte[19];
        var nameEncoded = Win1251.GetBytes(ch.Name);
        Array.Copy(nameEncoded, nameEncodedWithPadding, Math.Min(nameEncoded.Length, 19));

        ushort MaxHP = (ushort)ch.MaxHp;
        ushort MaxMP = (ushort)ch.MaxMp;
        ushort Strength = (ushort)ch.Strength;
        ushort Agility = (ushort)ch.Agility;
        ushort Accuracy = (ushort)ch.Accuracy;
        ushort Endurance = (ushort)ch.Endurance;
        ushort Earth = (ushort)ch.Earth;
        ushort Air = (ushort)ch.Air;
        ushort Water = (ushort)ch.Water;
        ushort Fire = (ushort)ch.Fire;
        ushort PDef = 0;
        ushort MDef = 0;
        byte Karma = 3; // Neutral
        ushort MaxSatiety = 100;
        ushort TitleLevelMinusOne = 0;
        ushort DegreeLevelMinusOne = 0;
        uint TitleXP = 0;
        uint DegreeXP = 0;
        ushort CurrentSatiety = 50;
        ushort CurrentHP = (ushort)ch.Hp;
        ushort CurrentMP = (ushort)ch.Mp;
        ushort AvailableTitleStats = 4;
        ushort AvailableDegreeStats = 4;
        bool IsGenderFemale = ch.IsFemale;
        byte FaceType = ch.FaceType;
        byte HairStyle = ch.HairStyle;
        byte HairColor = ch.HairColor;
        byte Tattoo = ch.Tattoo;
        byte BootModelId = 0;
        byte PantsModelId = 0;
        byte ArmorModelId = 0;
        byte HelmetModelId = 0;
        byte GlovesModelId = 0;
        bool IsNotQueuedForDeletion = true;

        var hpMax1 = (byte)(((MaxHP & 0b111111) << 2) + 1);
        var hpMax2 = (byte)((MaxHP & 0b11111111000000) >> 6);
        var mpMax1 = (byte)(((MaxMP & 0b111111) << 2) + ((MaxHP & 0b1100000000000000) >> 14));
        var mpMax2 = (byte)((MaxMP & 0b11111111000000) >> 6);
        var str1 = (byte)(((Strength & 0b111111) << 2) + ((MaxMP & 0b1100000000000000) >> 14));
        var str2 = (byte)((Strength & 0b11111111000000) >> 6);
        var agi1 = (byte)(((Agility & 0b111111) << 2) + ((Strength & 0b1100000000000000) >> 14));
        var agi2 = (byte)((Agility & 0b11111111000000) >> 6);
        var acc1 = (byte)(((Accuracy & 0b111111) << 2) + ((Agility & 0b1100000000000000) >> 14));
        var acc2 = (byte)((Accuracy & 0b11111111000000) >> 6);
        var end1 = (byte)(((Endurance & 0b111111) << 2) + ((Accuracy & 0b1100000000000000) >> 14));
        var end2 = (byte)((Endurance & 0b11111111000000) >> 6);
        var ert1 = (byte)(((Earth & 0b111111) << 2) + ((Endurance & 0b1100000000000000) >> 14));
        var ert2 = (byte)((Earth & 0b11111111000000) >> 6);
        var air1 = (byte)(((Air & 0b111111) << 2) + ((Earth & 0b1100000000000000) >> 14));
        var air2 = (byte)((Air & 0b11111111000000) >> 6);
        var wat1 = (byte)(((Water & 0b111111) << 2) + ((Air & 0b1100000000000000) >> 14));
        var wat2 = (byte)((Water & 0b11111111000000) >> 6);
        var fir1 = (byte)(((Fire & 0b111111) << 2) + ((Water & 0b1100000000000000) >> 14));
        var fir2 = (byte)((Fire & 0b11111111000000) >> 6);
        var pd1 = (byte)(((PDef & 0b111111) << 2) + ((Fire & 0b1100000000000000) >> 14));
        var pd2 = (byte)((PDef & 0b11111111000000) >> 6);
        var md1 = (byte)(((MDef & 0b111111) << 2) + ((PDef & 0b1100000000000000) >> 14));
        var md2 = (byte)((MDef & 0b11111111000000) >> 6);
        var krm1 = (byte)(((((byte)Karma) & 0b111111) << 2) + ((MDef & 0b1100000000000000) >> 14));
        var satMax1 = (byte)(((MaxSatiety & 0b111111) << 2) + ((((byte)Karma) & 0b11000000) >> 14));
        var satMax2 = (byte)((MaxSatiety & 0b11111111000000) >> 6);
        var tit1 = (byte)(((TitleLevelMinusOne & 0b111111) << 2) + ((MaxSatiety & 0b1100000000000000) >> 14));
        var tit2 = (byte)((TitleLevelMinusOne & 0b11111111000000) >> 6);
        var deg1 = (byte)(((DegreeLevelMinusOne & 0b111111) << 2) + ((TitleLevelMinusOne & 0b1100000000000000) >> 14));
        var deg2 = (byte)((DegreeLevelMinusOne & 0b11111111000000) >> 6);
        var txp1 = (byte)(((TitleXP & 0b111111) << 2) + ((DegreeLevelMinusOne & 0b1100000000000000) >> 14));
        var txp2 = (byte)((TitleXP & 0b11111111000000) >> 6);
        var txp3 = (byte)((TitleXP & 0b1111111100000000000000) >> 14);
        var txp4 = (byte)((TitleXP & 0b111111110000000000000000000000) >> 22);
        var dxp1 = (byte)(((DegreeXP & 0b111111) << 2) + ((TitleXP & 0b11000000000000000000000000000000) >> 30));
        var dxp2 = (byte)((DegreeXP & 0b11111111000000) >> 6);
        var dxp3 = (byte)((DegreeXP & 0b1111111100000000000000) >> 14);
        var dxp4 = (byte)((DegreeXP & 0b111111110000000000000000000000) >> 22);
        var satCur1 = (byte)(((CurrentSatiety & 0b111111) << 2) +
                             ((DegreeXP & 0b11000000000000000000000000000000) >> 30));
        var satCur2 = (byte)((CurrentSatiety & 0b11111111000000) >> 6);
        var hpCur1 = (byte)(((CurrentHP & 0b111111) << 2) + ((CurrentSatiety & 0b1100000000000000) >> 14));
        var hpCur2 = (byte)((CurrentHP & 0b11111111000000) >> 6);
        var mpCur1 = (byte)(((CurrentMP & 0b111111) << 2) + ((CurrentHP & 0b1100000000000000) >> 14));
        var mpCur2 = (byte)((CurrentMP & 0b11111111000000) >> 6);
        var titleStats1 = (byte)(((AvailableTitleStats & 0b111111) << 2) + ((CurrentMP & 0b1100000000000000) >> 14));
        var titleStats2 = (byte)((AvailableTitleStats & 0b11111111000000) >> 6);
        var degStats1 = (byte)(((AvailableDegreeStats & 0b111111) << 2) +
                               ((AvailableTitleStats & 0b1100000000000000) >> 14));
        var degStats2 = (byte)((AvailableDegreeStats & 0b11111111000000) >> 6);
        var degStats3 = (byte)(((0b111010 << 2) + ((AvailableDegreeStats & 0b1100000000000000) >> 14)));
        var isFemale1 = (byte)((IsGenderFemale ? 1 : 0) << 2);
        var name1 = (byte)(((nameEncodedWithPadding[0] & 0b111111) << 2));
        var name2 = (byte)(((nameEncodedWithPadding[1] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[0] & 0b11000000) >> 6));
        var name3 = (byte)(((nameEncodedWithPadding[2] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[1] & 0b11000000) >> 6));
        var name4 = (byte)(((nameEncodedWithPadding[3] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[2] & 0b11000000) >> 6));
        var name5 = (byte)(((nameEncodedWithPadding[4] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[3] & 0b11000000) >> 6));
        var name6 = (byte)(((nameEncodedWithPadding[5] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[4] & 0b11000000) >> 6));
        var name7 = (byte)(((nameEncodedWithPadding[6] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[5] & 0b11000000) >> 6));
        var name8 = (byte)(((nameEncodedWithPadding[7] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[6] & 0b11000000) >> 6));
        var name9 = (byte)(((nameEncodedWithPadding[8] & 0b111111) << 2) +
                           ((nameEncodedWithPadding[7] & 0b11000000) >> 6));
        var name10 = (byte)(((nameEncodedWithPadding[9] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[8] & 0b11000000) >> 6));
        var name11 = (byte)(((nameEncodedWithPadding[10] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[9] & 0b11000000) >> 6));
        var name12 = (byte)(((nameEncodedWithPadding[11] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[10] & 0b11000000) >> 6));
        var name13 = (byte)(((nameEncodedWithPadding[12] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[11] & 0b11000000) >> 6));
        var name14 = (byte)(((nameEncodedWithPadding[13] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[12] & 0b11000000) >> 6));
        var name15 = (byte)(((nameEncodedWithPadding[14] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[13] & 0b11000000) >> 6));
        var name16 = (byte)(((nameEncodedWithPadding[15] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[14] & 0b11000000) >> 6));
        var name17 = (byte)(((nameEncodedWithPadding[16] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[15] & 0b11000000) >> 6));
        var name18 = (byte)(((nameEncodedWithPadding[17] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[16] & 0b11000000) >> 6));
        var name19 = (byte)(((nameEncodedWithPadding[18] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[17] & 0b11000000) >> 6));

        var face1 = (byte)(((FaceType & 0b111111) << 2) + ((nameEncodedWithPadding[18] & 0b11000000) >> 6));
        var hairStyle1 = (byte)(((HairStyle & 0b111111) << 2) + ((FaceType & 0b11000000) >> 6));
        var hairColor1 = (byte)(((HairColor & 0b111111) << 2) + ((HairStyle & 0b11000000) >> 6));
        var tattoo1 = (byte)(((Tattoo & 0b111111) << 2) + ((HairColor & 0b11000000) >> 6));
        var bootsModelId = (byte)(((BootModelId & 0b111111) << 2) + ((Tattoo & 0b11000000) >> 6));
        var pantsModelId = (byte)(((PantsModelId & 0b111111) << 2) + ((BootModelId & 0b11000000) >> 6));
        var armorModelId = (byte)(((ArmorModelId & 0b111111) << 2) + ((PantsModelId & 0b11000000) >> 6));
        var helmetModelId = (byte)(((HelmetModelId & 0b111111) << 2) + ((ArmorModelId & 0b11000000) >> 6));
        var glovesModelId1 = (byte)(((GlovesModelId & 0b111111) << 2) + ((HelmetModelId & 0b11000000) >> 6));
        var glovesModelId2 = (byte)((GlovesModelId & 0b11000000) >> 6);
        var isNotDeleted1 = (byte)(((IsNotQueuedForDeletion ? 1 : 0) << 1) + 1);

        var lookType = (byte)(IsNotQueuedForDeletion ? 0x79 : 0x19);

        var charDataBytes = new byte[]
        {
            0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, GetSecondByte(playerIndex),
            GetFirstByte(playerIndex), 0x08, 0x40, 0x60, lookType, hpMax1, hpMax2, mpMax1, mpMax2, str1, str2,
            agi1, agi2, acc1, acc2, end1, end2, ert1, ert2, air1, air2, wat1, wat2, fir1, fir2, pd1, pd2, md1,
            md2, krm1, satMax1, satMax2, tit1, tit2, deg1, deg2, txp1, txp2, txp3, txp4, dxp1, dxp2, dxp3,
            dxp4, satCur1, satCur2, hpCur1, hpCur2, mpCur1, mpCur2, titleStats1, titleStats2, degStats1,
            degStats2, degStats3, 0xC0, 0xC8, 0xC8, isFemale1, name1, name2, name3, name4, name5, name6,
            name7, name8, name9, name10, name11, name12, name13, name14, name15, name16, name17, name18,
            name19, face1, hairStyle1, hairColor1, tattoo1, bootsModelId, pantsModelId, armorModelId,
            helmetModelId, glovesModelId1, glovesModelId2, 0xC0, 0xC0, 0x00, 0xFC, 0xFF, 0xFF, 0xFF,
            isNotDeleted1, 0x00, 0x00, 0x00, 0x00
        };

        return charDataBytes;
    }

    /// <summary>
    /// Variable-length game data packet sent when player enters the world.
    /// Ported 1:1 from knelse CharacterData.ToGameDataByteArray()
    /// </summary>
    public static byte[] ToGameDataBytes(CharacterRecord ch, ushort playerIndex)
    {
        var nameEncoded = Win1251.GetBytes(ch.Name);
        var x = CoordsHelper.EncodeServerCoordinate(ch.X);
        var y = CoordsHelper.EncodeServerCoordinate(ch.Y);
        var z = CoordsHelper.EncodeServerCoordinate(ch.Z);
        var t = CoordsHelper.EncodeServerCoordinate(ch.Turn);
        var nameLen = nameEncoded.Length + 1;
        var data = new List<byte>
        {
            0x00,
            0x01,
            0x2C,
            0x01,
            0x00,
            0x00,
            0x04,
            GetSecondByte(playerIndex),
            GetFirstByte(playerIndex),
            0x08,
            0x00,
            (byte)(((nameLen & 0b111) << 5) + 2),
            (byte)(((nameEncoded[0] & 0b111) << 5) + ((nameLen & 0b11111000) >> 3))
        };

        for (var i = 1; i < nameEncoded.Length; i++)
            data.Add((byte)(((nameEncoded[i] & 0b111) << 5) + ((nameEncoded[i - 1] & 0b11111000) >> 3)));

        data.Add((byte)((nameEncoded[^1] & 0b11111000) >> 3));

        // No clan
        data.Add(0x00);
        data.Add(0x6E);

        data.Add(0x1A);
        data.Add(0x98);
        data.Add(0x18);
        data.Add(0x19);
        data.AddRange(x);
        data.AddRange(y);
        data.AddRange(z);
        data.AddRange(t);
        data.Add(0x37);
        data.Add(0x0D);
        data.Add(0x79);
        data.Add(0x00);
        data.Add(0xF0);

        // Equipment slots (all empty)
        // Helmet, Amulet, Shield, Armor, Gloves, Belt, LeftBracelet, RightBracelet,
        // TopLeftRing, TopRightRing, BottomLeftRing, BottomRightRing, Pants, Boots,
        // Spec, MapBook, RecipeBook, MantraBook, 2 empty, Inkpot, 1 empty (was: IslandToken),
        // Money, Travelbag, Key1, Key2, Mission,
        // Inventory 1-10
        for (var i = 0; i < 32; i++)
        {
            data.Add(0x00);
            data.Add(0x00);
        }

        // 20 zero bytes
        for (var i = 0; i < 20; i++) data.Add(0x00);

        // Special slots 1-9 + Ammo + SpeedhackMantra
        for (var i = 0; i < 11; i++)
        {
            data.Add(0x00);
            data.Add(0x00);
        }

        data.Add(0x04); // unknown marker
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x04); // unknown marker
        data.Add(0x00);
        data.Add(0xF0);

        // 150 zero bytes (reserved)
        for (var i = 0; i < 150; i++) data.Add(0x00);

        // Stats block
        ushort CurrentHP = (ushort)ch.Hp;
        ushort MaxHP = (ushort)ch.MaxHp;
        byte Karma = 3; // Neutral
        ushort DegreeLevelMinusOne = 0;
        ushort TitleLevelMinusOne = 0;
        int SpecLevelMinusOne = 0;
        int Money = (int)ch.Money;

        data.Add((byte)(((CurrentHP & 0b111) << 5) + 0b10011));
        data.Add((byte)((CurrentHP & 0b11111111000) >> 3));
        data.Add((byte)(((MaxHP & 0b11) << 6) + (0b100 << 3) + ((CurrentHP & 0b11100000000000) >> 11)));
        data.Add((byte)((MaxHP & 0b1111111100) >> 2));
        data.Add((byte)(((byte)Karma << 4) + ((MaxHP & 0b11110000000000) >> 10)));
        var toEncode = DegreeLevelMinusOne * 100 + TitleLevelMinusOne;
        data.Add((byte)(((toEncode & 0b111111) << 2) + 2));
        data.Add((byte)((toEncode & 0b11111111000000) >> 6));

        data.Add(0x80);

        // No spec
        data.Add(0x00);

        data.Add((byte)(((Money & 0b1111) << 4) + SpecLevelMinusOne));
        data.Add((byte)((Money & 0b111111110000) >> 4));
        data.Add((byte)((Money & 0b11111111000000000000) >> 12));
        data.Add((byte)((Money & 0b1111111100000000000000000000) >> 20));
        data.Add((byte)((Money & 0b11110000000000000000000000000000u) >> 28));

        var arr = data.ToArray();
        arr[0] = (byte)arr.Length;

        return arr;
    }

    /// <summary>
    /// Teleport packet, ported from knelse CharacterData.GetTeleportAndUpdateCharacterByteArray()
    /// </summary>
    public static byte[] GetTeleportPacket(WorldCoords coords, ushort playerIndex, string playerIndexStr)
    {
        var tp = new List<byte>(Convert.FromHexString($"AB002c01000004{playerIndexStr}0840E301"));
        var x = CoordsHelper.EncodeServerCoordinate(coords.X);
        var y = CoordsHelper.EncodeServerCoordinate(coords.Y);
        var z = CoordsHelper.EncodeServerCoordinate(coords.Z);
        var t = CoordsHelper.EncodeServerCoordinate(coords.Turn);
        var x_1 = ((x[0] & 0b111) << 5) + 0b00010;
        var x_2 = ((x[1] & 0b111) << 5) + ((x[0] & 0b11111000) >> 3);
        var x_3 = ((x[2] & 0b111) << 5) + ((x[1] & 0b11111000) >> 3);
        var x_4 = ((x[3] & 0b111) << 5) + ((x[2] & 0b11111000) >> 3);
        var y_1 = ((y[0] & 0b111) << 5) + ((x[3] & 0b11111000) >> 3);
        var y_2 = ((y[1] & 0b111) << 5) + ((y[0] & 0b11111000) >> 3);
        var y_3 = ((y[2] & 0b111) << 5) + ((y[1] & 0b11111000) >> 3);
        var y_4 = ((y[3] & 0b111) << 5) + ((y[2] & 0b11111000) >> 3);
        var z_1 = ((z[0] & 0b111) << 5) + ((y[3] & 0b11111000) >> 3);
        var z_2 = ((z[1] & 0b111) << 5) + ((z[0] & 0b11111000) >> 3);
        var z_3 = ((z[2] & 0b111) << 5) + ((z[1] & 0b11111000) >> 3);
        var z_4 = ((z[3] & 0b111) << 5) + ((z[2] & 0b11111000) >> 3);
        var t_1 = ((t[0] & 0b111) << 5) + ((z[3] & 0b11111000) >> 3);
        var t_2 = ((t[1] & 0b111) << 5) + ((t[0] & 0b11111000) >> 3);
        var t_3 = ((t[2] & 0b111) << 5) + ((t[1] & 0b11111000) >> 3);
        var t_4 = ((t[3] & 0b111) << 5) + ((t[2] & 0b11111000) >> 3);
        var t_5 = 0b10100000 + ((t[3] & 0b11111000) >> 3);
        tp.Add((byte)x_1);
        tp.Add((byte)x_2);
        tp.Add((byte)x_3);
        tp.Add((byte)x_4);
        tp.Add((byte)y_1);
        tp.Add((byte)y_2);
        tp.Add((byte)y_3);
        tp.Add((byte)y_4);
        tp.Add((byte)z_1);
        tp.Add((byte)z_2);
        tp.Add((byte)z_3);
        tp.Add((byte)z_4);
        tp.Add((byte)t_1);
        tp.Add((byte)t_2);
        tp.Add((byte)t_3);
        tp.Add((byte)t_4);
        tp.Add((byte)t_5);

        tp.AddRange(Convert.FromHexString(
            "200839EDA800C80000000B40E74520F74210793188BC20245B14222F0C6071000B045824C04201160BB0608045032C1C64F1200B085844C042021613B0A08045052C2C6071010B4CE44526F2421379B1010B0E5874C0C203161FB0008145082C446031220B125994C0C2041627B64081450A2C5460B10AB160C1450B2E5C6031030B1A58D4C0C2061B1202F602"));

        return tp.ToArray();
    }
}

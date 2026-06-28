using SphereServer.Protocol;

namespace SphereServer.Helpers;

/// <summary>
/// Test data helpers, ported from knelse TestHelper.cs
/// </summary>
public static class TestHelper
{
    public static byte[] GetNewPlayerDungeonMobData(WorldCoords dungeonEntranceCoords)
    {
        var mobX = dungeonEntranceCoords.X - 50;
        var mobY = dungeonEntranceCoords.Y;
        var mobZ = dungeonEntranceCoords.Z + 19.5;
        var mobT = -2;
        var entity = new EntitySpawnData
        {
            ID = 54321,
            Unknown = 1260,
            X = mobX,
            Y = mobY,
            Z = mobZ,
            Turn = mobT,
            HP = 1009,
            TypeID = 1241,
            Level = 0
        };

        return PacketBuilder.Build(entity.ToByteArray(), 1);
    }

    public static byte[] GetTestMobData()
    {
        var entity = new EntitySpawnData
        {
            ID = 0xB19F,
            Unknown = 1260,
            X = 2310,
            Y = 159.5,
            Z = -2500,
            Turn = 0,
            HP = 1009,
            TypeID = 1069,
            Level = 0
        };

        return PacketBuilder.Build(entity.ToByteArray(), 1);
    }
}

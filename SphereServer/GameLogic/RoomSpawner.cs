using System.Globalization;

namespace SphereServer.GameLogic;

/// <summary>
/// Parsed room object from params/rooms/*.mbd
/// </summary>
public class RoomObject
{
    public string ModelName = "";
    public double X, Y, Z;
    public double R1, R2, R3; // rotation/extra
}

/// <summary>
/// Spawns dungeon room objects by reading .mbd files and building spawn packets
/// using dungeon.spdp / dungeon_entrance.spdp templates.
/// </summary>
public static class RoomSpawner
{
    /// <summary>
    /// Parse a room .mbd file: 4-byte entity count, then 44-byte entries
    /// (20 bytes name + 12 bytes XYZ floats + 12 bytes rotation floats)
    /// </summary>
    public static List<RoomObject> ParseRoomFile(string path)
    {
        var data = File.ReadAllBytes(path);
        var objects = new List<RoomObject>();

        if (data.Length < 4) return objects;

        var count = BitConverter.ToInt32(data, 0);
        var offset = 4;

        for (var i = 0; i < count && offset + 44 <= data.Length; i++)
        {
            // 20 bytes: model name (null-terminated ASCII)
            var nameEnd = Array.IndexOf(data, (byte)0, offset, 20);
            var nameLen = nameEnd >= 0 ? nameEnd - offset : 20;
            var name = System.Text.Encoding.ASCII.GetString(data, offset, nameLen);
            offset += 20;

            // 3 floats: X, Y, Z
            var x = BitConverter.ToSingle(data, offset); offset += 4;
            var y = BitConverter.ToSingle(data, offset); offset += 4;
            var z = BitConverter.ToSingle(data, offset); offset += 4;

            // 3 more values
            var r1 = BitConverter.ToSingle(data, offset); offset += 4;
            var r2 = BitConverter.ToSingle(data, offset); offset += 4;
            var r3 = BitConverter.ToSingle(data, offset); offset += 4;

            objects.Add(new RoomObject
            {
                ModelName = name, X = x, Y = y, Z = z,
                R1 = r1, R2 = r2, R3 = r3
            });
        }

        Console.WriteLine($"RoomSpawner: parsed {objects.Count} objects from {Path.GetFileName(path)}");
        return objects;
    }

    /// <summary>
    /// Map model name prefix to .spdp template file and object type
    /// </summary>
    private static (string spdpFile, int objectType)? GetSpdpForModel(string modelName)
    {
        var lower = modelName.ToLowerInvariant();

        // Room geometry (tn2_*, tn3_*, rd_*)
        if (lower.StartsWith("tn2_") || lower.StartsWith("tn3_") || lower.StartsWith("rd_"))
            return ("dungeon", 96);

        // Door
        if (lower == "edoor" || lower.StartsWith("edoor"))
            return ("dungeon_entrance", 65);

        // Chest
        if (lower.StartsWith("ct_chest"))
            return ("chest_in_dungeon", 417);

        // NPC (char*)
        if (lower.StartsWith("char"))
            return ("dungeon", 96); // spawn as generic dungeon object for now

        // Skip "empty" and unknown
        return null;
    }

    /// <summary>
    /// Build spawn packets for all objects in a room.
    /// </summary>
    public static byte[] BuildRoomSpawnPackets(List<RoomObject> objects, string spawnDataPath,
        ushort startEntityId = 50)
    {
        using var ms = new MemoryStream();
        var entityId = startEntityId;

        foreach (var obj in objects)
        {
            if (obj.ModelName == "empty") continue;

            var mapping = GetSpdpForModel(obj.ModelName);
            if (mapping == null)
            {
                Console.WriteLine($"RoomSpawner: skipping unknown model [{obj.ModelName}]");
                continue;
            }

            var (spdpFile, objectType) = mapping.Value;

            try
            {
                var spdpPath = Path.Combine(spawnDataPath, spdpFile + ".spdp");
                var parts = SpdpLoader.LoadFromFile(spdpPath);

                // Clone parts
                parts = parts.Select(p => new PacketPart(
                    p.Name, p.Type, p.BitLength,
                    new List<int>(p.Value)
                )).ToList();

                SpdpLoader.UpdateEntityId(parts, entityId);
                SpdpLoader.UpdateCoordinates(parts, obj.X, obj.Y, obj.Z,
                    (int)(obj.R1 * 256 / (2 * Math.PI)) & 0xFF); // angle from radians if needed

                // Update object_type if it has that field
                var otPart = parts.FirstOrDefault(p => p.Name == "object_type");
                if (otPart != null)
                    SpdpLoader.UpdateIntValue(parts, "object_type", objectType, 10);

                var packet = SpdpLoader.GetBytesToWrite(parts);
                ms.Write(packet);
                entityId++;

                Console.WriteLine($"RoomSpawner: spawned [{obj.ModelName}] at ({obj.X:F0}, {obj.Y:F0}, {obj.Z:F0})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RoomSpawner: error spawning [{obj.ModelName}]: {ex.Message}");
            }
        }

        return ms.ToArray();
    }
}

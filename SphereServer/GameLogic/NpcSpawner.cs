using System.Globalization;

namespace SphereServer.GameLogic;

/// <summary>
/// Parsed NPC spawn entry from npc.txt.
/// </summary>
public class NpcSpawnEntry
{
    public ushort Id;
    public string NpcType = "";
    public double X, Y, Z;
    public int Angle;
    public int SubtypeId;   // name_id for the packet
    public int ItemTierMin;
    public string ModelName = "";
    public int ItemTierMax;
    public string IconName = "";
    public int NpcTradeType;
}

/// <summary>
/// Loads NPC spawn data from npc.txt and builds spawn packets using .spdp definitions.
///
/// NPC types from npc.txt col 1, mapped to object type IDs and .spdp files:
///   NpcBanker      -> type 225, npc_banker.spdp
///   NpcTrade       -> type 213, npc_trade.spdp
///   NpcGuilder     -> type 216, npc_guilder.spdp
///   NpcQuestTitle  -> type 205, npc_quest_title.spdp
///   NpcQuestDegree -> type 209, npc_quest_degree.spdp
///   NpcQuestKarma  -> type 208, npc_quest_karma.spdp
///   NpcTournament  -> type 239, npc_tournament.spdp
/// </summary>
public static class NpcSpawner
{
    /// <summary>
    /// Map from NPC type string (in npc.txt) to .spdp file name (without extension).
    /// </summary>
    private static readonly Dictionary<string, string> NpcTypeToSpdpFile = new()
    {
        ["NpcBanker"] = "npc_banker",
        ["NpcTrade"] = "npc_trade",
        ["NpcGuilder"] = "npc_guilder",
        ["NpcQuestTitle"] = "npc_quest_title",
        ["NpcQuestDegree"] = "npc_quest_degree",
        ["NpcQuestKarma"] = "npc_quest_karma",
        ["NpcTournament"] = "npc_tournament",
    };

    /// <summary>
    /// Map from NPC type string to NpcTradeType value used in the packet
    /// (from knelse NpcTypeToNpcTradeTypeSph).
    /// Only relevant for types whose .spdp has a npc_trade_type field.
    /// </summary>
    private static readonly Dictionary<string, int> NpcTypeToTradeType = new()
    {
        ["NpcBanker"] = 0,
        ["NpcTrade"] = 0,      // overridden by col 12
        ["NpcGuilder"] = 1,
        ["NpcQuestTitle"] = 4,
        ["NpcQuestDegree"] = 2,
        ["NpcQuestKarma"] = 3,
        ["NpcTournament"] = 13,
    };

    /// <summary>
    /// Default model names for NPC types that don't have model names in npc.txt.
    /// From knelse NpcsFill.ApplyNpcTypeFixups.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultModelNames = new()
    {
        ["NpcBanker"] = "npc29d",
        ["NpcQuestTitle"] = "npc08",
        ["NpcQuestDegree"] = "npc59",
        ["NpcQuestKarma"] = "npc58",
    };

    /// <summary>
    /// Default icon names for NPC types that don't have icon names in npc.txt.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultIconNames = new()
    {
        ["NpcBanker"] = "npc_banker",
    };

    /// <summary>
    /// Cache of loaded .spdp templates keyed by spdp file name.
    /// </summary>
    private static readonly Dictionary<string, List<PacketPart>> SpdpCache = new();

    /// <summary>
    /// Load all NPC spawn entries from npc.txt.
    /// </summary>
    public static List<NpcSpawnEntry> LoadNpcData(string npcFilePath)
    {
        var entries = new List<NpcSpawnEntry>();
        var lines = File.ReadAllLines(npcFilePath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8) continue;

            var entry = new NpcSpawnEntry();

            // Col 0: ID (hex)
            if (!ushort.TryParse(parts[0].Trim(), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out entry.Id))
                continue;

            // Col 1: NpcType
            entry.NpcType = parts[1].Trim();
            if (!NpcTypeToSpdpFile.ContainsKey(entry.NpcType))
                continue;

            // Col 2: SpawnMode (always FULL_SPAWN, skip)
            // Col 3-5: X Y Z
            if (!double.TryParse(parts[3].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out entry.X))
                continue;
            if (!double.TryParse(parts[4].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out entry.Y))
                continue;
            if (!double.TryParse(parts[5].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out entry.Z))
                continue;

            // Col 6: Angle
            if (!int.TryParse(parts[6].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out entry.Angle))
                continue;

            // Col 7: SubtypeId (name_id for packet)
            if (!int.TryParse(parts[7].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out entry.SubtypeId))
                continue;

            // Col 8: ItemTierMin
            if (parts.Length > 8)
                int.TryParse(parts[8].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out entry.ItemTierMin);

            // Col 9: ModelName (optional)
            if (parts.Length > 9)
                entry.ModelName = parts[9].Trim();

            // Col 10: ItemTierMax (optional)
            if (parts.Length > 10)
                int.TryParse(parts[10].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out entry.ItemTierMax);

            // Col 11: IconName (optional)
            if (parts.Length > 11)
                entry.IconName = parts[11].Trim();

            // Col 12: NpcTradeType (optional)
            if (parts.Length > 12)
                int.TryParse(parts[12].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out entry.NpcTradeType);

            entries.Add(entry);
        }

        Console.WriteLine($"NpcSpawner: loaded {entries.Count} NPC spawn entries");
        return entries;
    }

    /// <summary>
    /// Build a spawn packet for a single NPC.
    /// </summary>
    /// <param name="entry">NPC spawn data from npc.txt</param>
    /// <param name="localEntityId">Entity ID to use in the packet (client-local)</param>
    /// <param name="spawnDataPath">Path to the SpawnData directory containing .spdp files</param>
    public static byte[] BuildSpawnPacket(NpcSpawnEntry entry, ushort localEntityId, string spawnDataPath)
    {
        var spdpName = NpcTypeToSpdpFile[entry.NpcType];
        var parts = LoadSpdpTemplate(spdpName, spawnDataPath);

        // Update entity ID
        SpdpLoader.UpdateEntityId(parts, localEntityId);

        // Update coordinates and angle
        SpdpLoader.UpdateCoordinates(parts, entry.X, entry.Y, entry.Z, entry.Angle);

        // Update name_id (SubtypeId from npc.txt = name_id in packet)
        SpdpLoader.UpdateIntValue(parts, "name_id", entry.SubtypeId, 11);

        // Resolve model name
        var modelName = ResolveModelName(entry);
        var modelNameSph = modelName + "\0";

        // For NpcGuilder, pad model name to 16 chars
        if (entry.NpcType == "NpcGuilder")
        {
            modelNameSph = modelNameSph.PadRight(16, '\0');
        }

        // Update entity_type_name (model) if the .spdp has that field
        var hasEntityTypeName = parts.Any(p => p.Name == "entity_type_name");
        if (hasEntityTypeName)
        {
            SpdpLoader.UpdateStringValue(parts, "entity_type_name", modelNameSph,
                updateLength: true, lengthBitLength: 8);
        }

        // Resolve icon name
        var iconName = ResolveIconName(entry);
        var iconNameSph = iconName + "\0";

        // Update icon_name if the .spdp has that field
        var hasIconName = parts.Any(p => p.Name == "icon_name");
        if (hasIconName)
        {
            SpdpLoader.UpdateStringValue(parts, "icon_name", iconNameSph,
                updateLength: true, lengthBitLength: 8);
        }

        // Update npc_trade_type if the .spdp has that field
        var hasTradeType = parts.Any(p => p.Name == "npc_trade_type");
        if (hasTradeType)
        {
            var tradeType = entry.NpcTradeType > 0
                ? entry.NpcTradeType
                : NpcTypeToTradeType.GetValueOrDefault(entry.NpcType, 0);
            SpdpLoader.UpdateIntValue(parts, "npc_trade_type", tradeType, 4);
        }

        return SpdpLoader.GetBytesToWrite(parts);
    }

    /// <summary>
    /// Build spawn packets for all NPCs and return as a single concatenated byte array.
    /// Entity IDs are assigned sequentially starting from startEntityId.
    /// </summary>
    public static byte[] BuildAllSpawnPackets(string spawnDataPath, ushort startEntityId = 1)
    {
        var npcFilePath = Path.Combine(spawnDataPath, "npc.txt");
        var entries = LoadNpcData(npcFilePath);

        using var ms = new MemoryStream();
        var entityId = startEntityId;

        foreach (var entry in entries)
        {
            try
            {
                var packet = BuildSpawnPacket(entry, entityId, spawnDataPath);
                ms.Write(packet);
                entityId++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NpcSpawner: error building packet for NPC {entry.Id:X4} ({entry.NpcType}): {ex.Message}");
            }
        }

        Console.WriteLine($"NpcSpawner: built {entityId - startEntityId} NPC spawn packets");
        return ms.ToArray();
    }

    /// <summary>
    /// Load an .spdp template, cloning its parts so the original is not modified.
    /// Templates are cached on first load.
    /// </summary>
    private static List<PacketPart> LoadSpdpTemplate(string spdpName, string spawnDataPath)
    {
        if (!SpdpCache.TryGetValue(spdpName, out var template))
        {
            var path = Path.Combine(spawnDataPath, spdpName + ".spdp");
            template = SpdpLoader.LoadFromFile(path);
            SpdpCache[spdpName] = template;
        }

        // Clone parts so modifications don't affect the cached template
        return template.Select(p => new PacketPart(
            p.Name, p.Type, p.BitLength,
            new List<int>(p.Value)
        )).ToList();
    }

    /// <summary>
    /// Resolve the model name for an NPC, using npc.txt data or defaults.
    /// </summary>
    private static string ResolveModelName(NpcSpawnEntry entry)
    {
        // Use model from npc.txt if it starts with "npc"
        if (!string.IsNullOrEmpty(entry.ModelName) &&
            entry.ModelName.StartsWith("npc", StringComparison.OrdinalIgnoreCase))
        {
            return entry.ModelName;
        }

        // Fall back to type-specific defaults
        if (DefaultModelNames.TryGetValue(entry.NpcType, out var defaultModel))
        {
            return defaultModel;
        }

        // Last resort
        return "npc08";
    }

    /// <summary>
    /// Resolve the icon name for an NPC, using npc.txt data or defaults.
    /// </summary>
    private static string ResolveIconName(NpcSpawnEntry entry)
    {
        // Use icon from npc.txt if it starts with "npc_"
        if (!string.IsNullOrEmpty(entry.IconName) &&
            entry.IconName.StartsWith("npc_", StringComparison.OrdinalIgnoreCase))
        {
            return entry.IconName;
        }

        // Fall back to type-specific defaults
        if (DefaultIconNames.TryGetValue(entry.NpcType, out var defaultIcon))
        {
            return defaultIcon;
        }

        // If no icon specified, return empty (the .spdp default will be used)
        return "";
    }
}

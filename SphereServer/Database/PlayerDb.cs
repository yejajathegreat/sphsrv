using LiteDB;
using SphereServer.Auth;

namespace SphereServer.Database;

public class PlayerRecord
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public List<int> CharacterIds { get; set; } = [];
    public bool IsBanned { get; set; }
}

public class CharacterRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public double X { get; set; }
    public double Y { get; set; } = 150;
    public double Z { get; set; }
    public double Turn { get; set; }
    public int Hp { get; set; } = 100;
    public int MaxHp { get; set; } = 100;
    public int Mp { get; set; } = 100;
    public int MaxMp { get; set; } = 100;
    public int Strength { get; set; }
    public int Agility { get; set; }
    public int Accuracy { get; set; }
    public int Endurance { get; set; }
    public int Earth { get; set; }
    public int Air { get; set; }
    public int Water { get; set; }
    public int Fire { get; set; }
    public long Money { get; set; }
    public bool IsFemale { get; set; }
    public byte FaceType { get; set; }
    public byte HairStyle { get; set; }
    public byte HairColor { get; set; }
    public byte Tattoo { get; set; }
    public int SlotIndex { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// LiteDB-backed player/character storage.
/// </summary>
public class PlayerDb : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<PlayerRecord> _players;
    private readonly ILiteCollection<CharacterRecord> _characters;

    public PlayerDb(string dbPath = "sphere.db")
    {
        _db = new LiteDatabase($"Filename={dbPath};Connection=shared;");
        _players = _db.GetCollection<PlayerRecord>("Players");
        _characters = _db.GetCollection<CharacterRecord>("Characters");

        _players.EnsureIndex(x => x.Login);
        _characters.EnsureIndex(x => x.Name);

        Console.WriteLine($"[DB] Initialized: {dbPath}");
    }

    public PlayerRecord? Login(string login, string password, bool createIfNew = true)
    {
        var player = _players.FindOne(x => x.Login == login);

        if (player != null)
        {
            if (!PasswordHasher.Verify(password, player.PasswordHash))
            {
                Console.WriteLine($"[DB] Wrong password for [{login}]");
                return null;
            }

            if (player.IsBanned)
            {
                Console.WriteLine($"[DB] Player [{login}] is banned");
                return null;
            }

            Console.WriteLine($"[DB] Player [{login}] logged in (id={player.Id})");
            return player;
        }

        if (!createIfNew)
            return null;

        player = new PlayerRecord
        {
            Login = login,
            PasswordHash = PasswordHasher.Hash(password)
        };

        player.Id = _players.Insert(player);
        Console.WriteLine($"[DB] Created new player [{login}] (id={player.Id})");
        return player;
    }

    public PlayerRecord? GetPlayerById(int id) => _players.FindById(id);

    public CharacterRecord? GetCharacter(int id) => _characters.FindById(id);

    public List<CharacterRecord> GetCharacters(List<int> ids) =>
        ids.Select(id => _characters.FindById(id)).Where(c => c != null).ToList()!;

    public CharacterRecord CreateCharacter(string name, int playerId, int slotIndex = 0,
        bool isFemale = false, byte faceType = 0, byte hairStyle = 0, byte hairColor = 0, byte tattoo = 0)
    {
        var character = new CharacterRecord
        {
            Name = name,
            IsFemale = isFemale,
            FaceType = faceType,
            HairStyle = hairStyle,
            HairColor = hairColor,
            Tattoo = tattoo,
            SlotIndex = slotIndex
        };
        character.Id = _characters.Insert(character);

        var player = _players.FindById(playerId);
        if (player != null)
        {
            player.CharacterIds.Add(character.Id);
            _players.Update(player);
        }

        Console.WriteLine($"[DB] Created character [{name}] (id={character.Id}) for player {playerId}");
        return character;
    }

    public void DeleteCharacter(int characterId, int playerId)
    {
        _characters.Delete(characterId);
        var player = _players.FindById(playerId);
        if (player != null)
        {
            player.CharacterIds.Remove(characterId);
            _players.Update(player);
        }
        Console.WriteLine($"[DB] Deleted character id={characterId} for player {playerId}");
    }

    public bool IsNameTaken(string name) => _characters.Exists(x => x.Name == name);

    public void SaveCharacter(CharacterRecord character) => _characters.Update(character);

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}

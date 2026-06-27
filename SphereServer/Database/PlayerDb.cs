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
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Hp { get; set; } = 200;
    public int MaxHp { get; set; } = 200;
    public int Mp { get; set; } = 200;
    public int MaxMp { get; set; } = 200;
    public int Strength { get; set; } = 16;
    public int Agility { get; set; } = 16;
    public int Accuracy { get; set; } = 16;
    public int Endurance { get; set; } = 16;
    public int Earth { get; set; } = 16;
    public int Air { get; set; } = 16;
    public int Water { get; set; } = 16;
    public int Fire { get; set; } = 16;
    public long Money { get; set; }
    public bool IsFemale { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// LiteDB-backed player/character storage.
/// Compatible with SphereEmu DB format.
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

    public CharacterRecord? GetCharacter(int id) => _characters.FindById(id);

    public List<CharacterRecord> GetCharacters(List<int> ids) =>
        ids.Select(id => _characters.FindById(id)).Where(c => c != null).ToList()!;

    public CharacterRecord CreateCharacter(string name, int playerId)
    {
        var character = new CharacterRecord { Name = name };
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

    public bool IsNameTaken(string name) => _characters.Exists(x => x.Name == name);

    public void SaveCharacter(CharacterRecord character) => _characters.Update(character);

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}

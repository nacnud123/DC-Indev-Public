namespace VoxelEngine.Server;

public sealed class ServerProperties
{
    public int ServerPort = 25565;
    public string LevelName = "World";
    public string ServerName = "A voxel engine server";
    public string Motd = "Welcome!";
    public int MaxPlayers = 20;
    public int ViewDistance = 8;
    public bool WhitelistEnabled = false;
    public bool SpawnMonsters = true;
    public bool Pvp = true;
    public long LevelSeed = 0;

    private readonly HashSet<string> mOps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> mBanned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> mWhitelist = new(StringComparer.OrdinalIgnoreCase);

    public static ServerProperties LoadOrCreate(string path)
    {
        var props = new ServerProperties();

        if (!File.Exists(path))
        {
            props.Save(path);
            Console.WriteLine($"Generated {path}");
        }
        else
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith('#') || !line.Contains('='))
                    continue;

                var kv = line.Split('=', 2);
                props.Apply(kv[0].Trim(), kv[1].Trim());
            }
        }

        props.LoadList("ops.txt", props.mOps);
        props.LoadList("banned-players.txt", props.mBanned);
        props.LoadList("white-list.txt", props.mWhitelist);
        return props;
    }

    public void Save(string path) => File.WriteAllLines(path, new[]
    {
        $"server-port={ServerPort}", $"level-name={LevelName}", $"server-name={ServerName}",
        $"motd={Motd}", $"max-players={MaxPlayers}", $"view-distance={ViewDistance}",
        $"white-list={WhitelistEnabled}", $"spawn-monsters={SpawnMonsters}", $"pvp={Pvp}",
        $"level-seed={LevelSeed}",
    });

    public bool IsOp(string name) => mOps.Contains(name);
    public bool IsBanned(string name) => mBanned.Contains(name);
    public bool IsWhitelisted(string name) => mWhitelist.Contains(name);

    public void AddOp(string name)
    {
        mOps.Add(name);
        SaveList("ops.txt", mOps);
    }

    public void Ban(string name)
    {
        mBanned.Add(name);
        SaveList("banned-players.txt", mBanned);
    }

    private static void SaveList(string path, HashSet<string> set) => File.WriteAllLines(path, set);

    private void LoadList(string path, HashSet<string> into)
    {
        if (File.Exists(path))
            foreach (var l in File.ReadAllLines(path))
                if (!string.IsNullOrWhiteSpace(l))
                    into.Add(l.Trim());
    }

    private void Apply(string key, string value)
    {
        switch (key)
        {
            case "server-port": ServerPort = Int(value, ServerPort); break;
            case "level-name": LevelName = value; break;
            case "server-name": ServerName = value; break;
            case "motd": Motd = value; break;
            case "max-players": MaxPlayers = Int(value, MaxPlayers); break;
            case "view-distance": ViewDistance = Int(value, ViewDistance); break;
            case "white-list": WhitelistEnabled = Bool(value, WhitelistEnabled); break;
            case "spawn-monsters": SpawnMonsters = Bool(value, SpawnMonsters); break;
            case "pvp": Pvp = Bool(value, Pvp); break;
            case "level-seed": LevelSeed = long.TryParse(value, out var s) ? s : LevelSeed; break;
            default: Console.WriteLine($"Unknown property '{key}' in server.properties"); break;
        }
    }

    private static int Int(string v, int fallback) => int.TryParse(v, out var r) ? r : fallback;
    private static bool Bool(string v, bool fallback) => bool.TryParse(v, out var r) ? r : fallback;
}
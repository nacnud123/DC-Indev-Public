using System.Xml.Serialization;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Net;
using VoxelEngine.Saving;

namespace VoxelEngine.Server;

[Serializable]
public sealed class PlayerSaveData
{
    public string Name = "";
    public float X, Y, Z, Yaw, Pitch;
    public int Health = 20;
    public bool IsOp;
    public List<SavedSlot> Inventory = new(); // SavedSlot already exists - PlayerInventory uses it
}

public sealed partial class DuncanCraftServer
{
    private string PlayerFilePath(string name) =>
        Path.Combine(mProps.LevelName, "players", $"{SanitiseName(name)}.xml");

    private static string SanitiseName(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        return cleaned.Length == 0 ? "player" : cleaned[..Math.Min(cleaned.Length, 32)];
    }

    private ServerPlayer CreateOrLoadPlayer(string username, ServerConnection conn)
    {
        var player = new ServerPlayer
        {
            Name = username,
            Connection = conn,
            EntityId = Entity.AllocateId(),
            IsOp = mProps.IsOp(username),
            ViewDistanceChunks = mProps.ViewDistance,
        };

        string path = PlayerFilePath(username);

        if (File.Exists(path))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open);
                var data = (PlayerSaveData)new XmlSerializer(typeof(PlayerSaveData)).Deserialize(stream)!;

                // aspectRatio is a camera field the server never reads; 1f avoids a degenerate
                // projection matrix if anything ever does touch it.
                player.Entity = new Player(new Vector3(data.X, data.Y, data.Z), aspectRatio: 1f);
                player.Entity.Health = data.Health;
                player.Entity.AssignNetworkId(player.EntityId);
                player.Yaw = data.Yaw;
                player.Pitch = data.Pitch;

                if (data.Inventory.Count > 0)
                    player.Inventory.LoadFromSlots(data.Inventory);

                AttachInventoryWindow(player);
                MarkAsProxy(player);
                return player;             // loaded - do NOT fall through to the fresh-spawn path
            }
            catch (Exception e)
            {
                mLog.Log(LogLevel.Warning, $"Could not read {path}: {e.Message}. Starting fresh.");
            }
        }

        // First join, or the save was unreadable.
        player.Entity = new Player(FindSafeSpawn(mSpawn), aspectRatio: 1f);
        player.Entity.AssignNetworkId(player.EntityId);
        GiveStarterKit(player);
        AttachInventoryWindow(player);
        MarkAsProxy(player);
        return player;
    }

    /// What a brand new player arrives with. Only on a first join - a returning player's save
    /// already has whatever they made of it, and re-granting this would print money.
    private static readonly ItemStack[] StarterKit =
    [
        ItemStack.FromBlock(BlockType.Wood, 16),
        ItemStack.FromBlock(BlockType.CobbleStone, 16),
        ItemStack.FromBlock(BlockType.Torch, 8),
    ];

    private static void GiveStarterKit(ServerPlayer player)
    {
        foreach (var stack in StarterKit)
            player.Inventory.TryAdd(stack);
    }

    /// The client owns where its own player is; the server records it. Ticking the entity here
    /// would run a second, input-less simulation of the same player and apply its own fall damage.
    private void MarkAsProxy(ServerPlayer player)
    {
        player.Entity.IsRemoteProxy = true;
        player.Entity.NetTargetPosition = player.Position;
        player.LastY = player.Position.Y;

        // Mob AI calls TakeDamage on this entity; DamagePlayer is what turns that into armour,
        // a health packet and, if it kills them, a death.
        player.Entity.DamageHandler = amount => DamagePlayer(player, amount, "was slain by a monster");
    }

    /// Window 0 is always open and never in the window dictionary, so it's built once per player.
    private static void AttachInventoryWindow(ServerPlayer player) =>
        player.InventoryWindow = new WindowSession
        {
            Id = 0,
            Kind = WindowKind.PlayerInventory,
            Viewer = player,
        };

    private PlayerSaveData SnapshotPlayer(ServerPlayer p) => new()
    {
        Name = p.Name,
        X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z,
        Yaw = p.Yaw, Pitch = p.Pitch,
        Health = p.Entity.Health,
        IsOp = p.IsOp,
        Inventory = p.Inventory.SaveToSlots(),
    };
    
    private void WritePlayerFile(PlayerSaveData data)
    {
        string path = PlayerFilePath(data.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write-then-move: a server killed mid-write leaves the previous file intact rather than
        // a half-written one that throws on next login.
        string tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create))
            new XmlSerializer(typeof(PlayerSaveData)).Serialize(stream, data);
        File.Move(tmp, path, overwrite: true);
    }

    private void SavePlayer(ServerPlayer p) => WritePlayerFile(SnapshotPlayer(p));
}
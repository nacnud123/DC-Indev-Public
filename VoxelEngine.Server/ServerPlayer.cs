using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Server;

public sealed class ServerPlayer
{
    public int EntityId;
    public string Name = "";
    public ServerConnection Connection = null;
    public Player Entity = null!;
    public PlayerInventory Inventory = new();
    
    public Vector3 Position
    {
        get => Entity.Position;
        set => Entity.Position = value;
    }

    public float Yaw, Pitch;
    public short HeldSlot;
    public int ViewDistanceChunks = 8;
    public bool IsOp;

    // Stage 10: the server owns every slot this player can see.
    public byte OpenWindowId;
    public ItemStack? Cursor;                          // the stack floating on their mouse
    public readonly CraftingGrid InventoryGrid = new(2, 2);

    /// Window 0 - always open, never in the server's window dictionary.
    public WindowSession InventoryWindow = null!;

    // Movement validation, following b1.7.3's NetServerHandler: the last position the server
    // accepted, and whether we are still waiting for the client to acknowledge a teleport. While
    // HasMoved is false, position packets are in-flight from before the teleport and are ignored.
    public Vector3 LastGoodPosition;
    public bool HasMoved = true;

    // Stage 11: health and liveness are the server's business now.
    public bool IsDead;
    public float FallDistance;
    public float LastY;
    public int HurtCooldownTicks;                      // server-side i-frames, in ticks

    // Drowning and burning state. The client tracks its own copies for the HUD, but only these
    // decide damage - see TickEnvironmentalDamage.
    public int BreathTicks;
    public int DrownCooldownTicks;
    public int FireTicks;
    public int LavaCooldownTicks;
    public int BurnCooldownTicks;

    /// Full lungs, not on fire. For a fresh login and for a respawn.
    public void ResetEnvironment(int breathTicks)
    {
        BreathTicks = breathTicks;
        DrownCooldownTicks = 0;
        FireTicks = 0;
        LavaCooldownTicks = 0;
        BurnCooldownTicks = 0;
    }
    public long LastKeepAlive = Environment.TickCount64;

    // Where the client says it started digging, and when. The server times the break against it.
    public Vector3i? DiggingAt;
    public long DiggingSinceTick;

    public readonly HashSet<ChunkCoord> SentChunks = new();
    public readonly HashSet<int> TrackedEntities = new();

    /// Dropped items are tracked separately from mobs: TrackMobsFor prunes any id in
    /// TrackedEntities that isn't a mob it can currently see, so drops sharing that set would be
    /// destroyed on the client every tick.
    public readonly HashSet<int> TrackedDrops = new();
}
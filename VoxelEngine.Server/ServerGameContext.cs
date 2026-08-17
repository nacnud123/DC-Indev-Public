// The server's IGameContext. Shared world code reaches its host through GameContext.Current, and
// LightingEngine's constructor reads it, so this must be bound before the first World exists. | Stage 4

using VoxelEngine.Audio;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Particles;
using VoxelEngine.Terrain;

namespace VoxelEngine.Server;

/// <summary>
/// Headless host. Most of IGameContext is singleplayer UI that a server has no answer for, so the
/// container methods are no-ops and the local-player members are null - server code uses
/// World.Players instead.
/// </summary>
public sealed class ServerGameContext : IGameContext
{
    private readonly DuncanCraftServer mServer;

    public ServerGameContext(DuncanCraftServer server, WorldGenSettings settings)
    {
        mServer = server;
        GetWorldGenSettings = settings;
    }

    /// Set once the server has built its world. Not in the constructor: LightingEngine reads this
    /// context while World's constructor is still running, so the binding has to exist first.
    public World? World;

    public World GetWorld => World!;

    /// Whoever the server is currently acting on behalf of, set for the duration of one handler.
    /// Item code (ItemBow, and anything else that reaches for the "local" player) is written for a
    /// game with exactly one player; on a server it has to be told which one, and only while that
    /// player's packet is being handled. Null the rest of the time, so anything that reads it
    /// outside a handler still fails loudly rather than acting for an arbitrary player.
    internal ServerPlayer? ActingPlayer;

    /// The player whose packet is being handled, or null outside a handler.
    public Player GetPlayer => ActingPlayer?.Entity!;

    public Random GameRandom { get; } = new();

    public IAudioManager AudioManager { get; } = new NullAudioManager();
    public IParticleSpawner ParticleSystem { get; } = new NullParticleSpawner();

    public PlayerInventory? PlayerInventory => ActingPlayer?.Inventory;
    public IHotbar? Hotbar => null;

    public bool IsCreative => false;

    public float TimeOfDay => mServer.TimeOfDay;

    public WorldGenSettings GetWorldGenSettings { get; }

    /// The server is always simulating; it has no menus to be in.
    public GameState CurrentState => GameState.Playing;

    // Container screens are client UI. Shared block code calls these when a chest is right-clicked;
    // on a server the WindowSession work in Stage 10 replaces them entirely.
    public void OpenCrafting() { }
    public void CloseCrafting() { }
    public void OpenFurnace(Vector3i pos) { }
    public void CloseFurnace() { }
    public void OpenChest(Vector3i pos) { }
    public void CloseChest() { }
    public void OpenDoubleChest(Vector3i canonicalPos) { }
    public void CloseDoubleChest() { }
}

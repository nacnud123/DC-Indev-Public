// The seam that replaced Game.Instance inside shared code. | DA

using VoxelEngine.Audio;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Particles;
using VoxelEngine.Terrain;

namespace VoxelEngine.Core;

/// <summary>
/// Everything shared world code needs from its host. The client's <c>Game</c> implements this;
/// a dedicated server implements its own, much smaller version.
///
/// This exists because <c>Game</c> lives in the client project and <c>Terrain</c>/<c>Items</c>/
/// <c>GameEntity</c> live in Common, so the ~200 <c>Game.Instance</c> reach-throughs that used to
/// compile fine are now across a project boundary. The interface is deliberately a transcript of
/// what those call sites actually used rather than a designed API - narrowing it is a later job,
/// and inventing members nobody calls would just be speculative coupling.
/// </summary>
public interface IGameContext
{
    /// <summary>
    /// The world being simulated. Declared non-nullable to match the <c>Game.Instance.GetWorld</c>
    /// it replaced: it is genuinely null at the main menu, and callers already dereference it
    /// unguarded. Making it nullable here would be more honest but would light up every existing
    /// call site with a warning without fixing anything - a separate cleanup, not Stage 1's job.
    /// </summary>
    World GetWorld { get; }

    /// <summary>
    /// The local player. On a server this is meaningless - server code should use
    /// <c>World.Players</c> instead, and anything still calling this is singleplayer-only logic.
    /// </summary>
    Player GetPlayer { get; }

    /// <summary>
    /// Shared RNG for gameplay randomness (mob drops, block ticks). Not used for terrain
    /// generation, which must stay a pure function of the seed - see InfiniteTerrainGenerator.
    /// </summary>
    Random GameRandom { get; }

    IAudioManager AudioManager { get; }
    IParticleSpawner ParticleSystem { get; }

    PlayerInventory? PlayerInventory { get; }
    IHotbar? Hotbar { get; }

    bool IsCreative { get; }

    /// <summary>[0,1): 0 = dawn, 0.25 = noon, 0.5 = dusk, 0.75 = midnight.</summary>
    float TimeOfDay { get; }

    WorldGenSettings GetWorldGenSettings { get; }

    GameState CurrentState { get; }

    // Container screens. Shared code opens these when a block is right-clicked and closes them
    // when the block is broken out from under an open screen. No-ops on a headless host.
    void OpenCrafting();
    void CloseCrafting();
    void OpenFurnace(Vector3i pos);
    void CloseFurnace();
    void OpenChest(Vector3i pos);
    void CloseChest();
    void OpenDoubleChest(Vector3i canonicalPos);
    void CloseDoubleChest();
}

/// <summary>
/// The two things shared code asks the hotbar UI. The concrete <c>Hotbar</c> is an ImGui screen,
/// so it can't cross into Common.
/// </summary>
public interface IHotbar
{
    int SelectedSlotIndex { get; }
    ItemStack? GetSelectedStack();
}

/// <summary>
/// Ambient access to the current host.
///
/// This is still a service locator, and it is still global mutable state - it is not an
/// improvement on <c>Game.Instance</c> in that respect. What it buys is the project boundary:
/// Common depends on an interface it owns instead of on the renderer, so the compiler can
/// enforce that no Silk.NET/ImGui/SFML type leaks into shared code. Threading the context
/// through constructors would be better still, and is a reasonable later refactor; doing it now
/// would touch every entity and block signature in the codebase for no Stage 1 benefit.
/// </summary>
public static class GameContext
{
    private static IGameContext? sCurrent;

    /// <summary>
    /// The active host. Throws rather than returning null, because every call site inherited
    /// from <c>Game.Instance</c> already assumed a host exists - a null here means the host
    /// forgot to call <see cref="Bind"/>, which is a startup bug, not a runtime condition.
    /// </summary>
    public static IGameContext Current =>
        sCurrent ?? throw new InvalidOperationException(
            "No IGameContext bound. The host (Game or the dedicated server) must call " +
            "GameContext.Bind() during startup, before any world or entity code runs.");

    /// <summary>True once a host is bound. For the rare caller that legitimately runs hostless.</summary>
    public static bool IsBound => sCurrent != null;

    public static void Bind(IGameContext context) => sCurrent = context;

    /// <summary>Clears the binding. Used on shutdown and between worlds in tests.</summary>
    public static void Unbind() => sCurrent = null;
}

// Which update/render path the client is running. Lives in Common because shared
// interaction code branches on it (opening a chest is a state change). | DA

namespace VoxelEngine.Core;

/// <summary>
/// Drives which per-frame update/render path <see cref="IGameContext"/>'s implementation runs.
/// Every state has a corresponding branch in the client's update and render dispatch and
/// typically its own ImGui screen instance. Only one state is active at a time; UI screens like
/// Inventory/Crafting/Furnace/Chest are "paused-but-visible" states layered over the (frozen)
/// world rather than separate scenes.
///
/// The dedicated server never leaves <see cref="Playing"/> - it has no screens - but shared code
/// still compares against these values, which is why the enum is here and not in the client.
/// </summary>
public enum GameState
{
    Playing,
    Paused,
    MainMenu,
    Inventory,
    Crafting,
    Furnace,
    Chest,
    DoubleChest,
    Loading,
    Died,
    Connecting,
    Disconnected
}

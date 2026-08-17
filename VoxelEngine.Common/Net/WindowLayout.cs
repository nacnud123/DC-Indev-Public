// How a window's slots are ordered on the wire. | Stage 10

using VoxelEngine.GameEntity;

namespace VoxelEngine.Net;

public enum WindowKind : byte
{
    PlayerInventory = 0,
    Chest = 1,
    Workbench = 2,
    Furnace = 3,
    DoubleChest = 4,
}

/// <summary>
/// The one place that knows how a window's slots are ordered: container first, then the player's
/// main inventory, then their hotbar. Server and client both call it, so a layout change can't
/// desynchronise them - two hand-matched layouts is the PacketLayout failure mode, and here a wrong
/// offset moves items between slots.
/// </summary>
public static class WindowLayout
{
    public static int ContainerSlotCount(WindowKind kind) => kind switch
    {
        WindowKind.PlayerInventory => 1 + 4,      // result + 2x2 grid
        WindowKind.Chest => 27,
        WindowKind.DoubleChest => 54,
        WindowKind.Workbench => 1 + 9,            // result + 3x3 grid
        WindowKind.Furnace => 3,                  // input, fuel, output
        _ => 0,
    };

    /// Container + main + hotbar. Armour is deliberately absent: nothing equips it yet, and adding
    /// it later means changing this one function.
    public static int TotalSlots(WindowKind kind) =>
        ContainerSlotCount(kind) + PlayerInventory.MAIN_SLOTS + PlayerInventory.HOTBAR_SLOTS;

    public static bool IsContainerSlot(WindowKind kind, int index) =>
        index >= 0 && index < ContainerSlotCount(kind);

    /// The slot you take a crafted item out of, or -1 for windows without one. You can take from it
    /// but never put into it, so callers must special-case it before the generic click path.
    public static int ResultSlot(WindowKind kind) =>
        kind is WindowKind.PlayerInventory or WindowKind.Workbench ? 0 : -1;

    /// Maps a PlayerInventory index onto its composite index, or -1 for slots no window shows
    /// (armour). The inverse of <see cref="ToInventoryIndex"/>.
    public static int FromInventoryIndex(WindowKind kind, int inventoryIndex)
    {
        int n = ContainerSlotCount(kind);

        if (inventoryIndex >= 0 && inventoryIndex < PlayerInventory.MAIN_SLOTS)
            return n + inventoryIndex;

        int hotbar = inventoryIndex - PlayerInventory.HOTBAR_START;

        return hotbar >= 0 && hotbar < PlayerInventory.HOTBAR_SLOTS
            ? n + PlayerInventory.MAIN_SLOTS + hotbar
            : -1;
    }

    /// Maps a composite index onto the player's own inventory index, or -1 if it's a container slot.
    public static int ToInventoryIndex(WindowKind kind, int index)
    {
        int n = ContainerSlotCount(kind);
        if (index < n)
            return -1;

        int offset = index - n;

        if (offset < PlayerInventory.MAIN_SLOTS)
            return offset;

        offset -= PlayerInventory.MAIN_SLOTS;

        return offset < PlayerInventory.HOTBAR_SLOTS
            ? PlayerInventory.HOTBAR_START + offset
            : -1;
    }
}

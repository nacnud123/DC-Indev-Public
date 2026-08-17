// One player's view of one container. | Stage 10

using VoxelEngine.BlockEntities;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;

namespace VoxelEngine.Server;

/// <summary>
/// The same chest can have several of these open at once - that's the multi-viewer case, and it's
/// why an accepted click re-sends the snapshot to all of them rather than just the clicker.
/// </summary>
public sealed class WindowSession
{
    public byte Id;
    public WindowKind Kind;
    public Vector3i BlockPosition;                     // which container; unused for PlayerInventory
    public ServerPlayer Viewer = null!;

    /// The workbench's grid lives on the session, not the world - Beta's workbench holds no state,
    /// so two players at one bench get separate grids.
    public CraftingGrid? Workbench;

    /// The grid this window crafts in, or null if it doesn't craft.
    public CraftingGrid? Grid => Kind switch
    {
        WindowKind.Workbench => Workbench,
        WindowKind.PlayerInventory => Viewer.InventoryGrid,
        _ => null,
    };

    public ItemStack?[] BuildComposite(World world)
    {
        var slots = new ItemStack?[WindowLayout.TotalSlots(Kind)];
        int n = WindowLayout.ContainerSlotCount(Kind);

        ReadContainer(world, slots, n);

        for (int i = 0; i < PlayerInventory.MAIN_SLOTS; i++)
            slots[n + i] = Viewer.Inventory.GetSlot(i);

        for (int i = 0; i < PlayerInventory.HOTBAR_SLOTS; i++)
            slots[n + PlayerInventory.MAIN_SLOTS + i] =
                Viewer.Inventory.GetSlot(PlayerInventory.HOTBAR_START + i);

        return slots;
    }

    public void WriteBack(World world, ItemStack?[] slots)
    {
        int n = WindowLayout.ContainerSlotCount(Kind);

        WriteContainer(world, slots, n);

        for (int i = 0; i < PlayerInventory.MAIN_SLOTS; i++)
            Viewer.Inventory.SetSlot(i, slots[n + i]);

        for (int i = 0; i < PlayerInventory.HOTBAR_SLOTS; i++)
            Viewer.Inventory.SetSlot(PlayerInventory.HOTBAR_START + i,
                                     slots[n + PlayerInventory.MAIN_SLOTS + i]);
    }

    // The server's BlockEntityManager entry is the only copy that exists now - the client no longer
    // creates its own.
    private void ReadContainer(World world, ItemStack?[] slots, int count)
    {
        switch (Kind)
        {
            case WindowKind.Chest:
            {
                var chest = BlockEntityManager.GetOrCreateChest(BlockPosition);
                for (int i = 0; i < count; i++) slots[i] = chest.GetSlot(i);
                break;
            }

            case WindowKind.DoubleChest:
            {
                var chest = BlockEntityManager.GetOrCreateDoubleChest(BlockPosition);
                for (int i = 0; i < count; i++) slots[i] = chest.GetSlot(i);
                break;
            }

            case WindowKind.Furnace:
            {
                var furnace = BlockEntityManager.GetOrCreateFurnace(BlockPosition);
                slots[0] = furnace.InputSlot;
                slots[1] = furnace.FuelSlot;
                slots[2] = furnace.OutputSlot;
                break;
            }

            case WindowKind.Workbench:
            case WindowKind.PlayerInventory:
            {
                var grid = Grid!;
                slots[0] = grid.Result;                // result first - see WindowLayout
                for (int i = 0; i < grid.Slots.Length; i++) slots[1 + i] = grid.Slots[i];
                break;
            }
        }
    }

    private void WriteContainer(World world, ItemStack?[] slots, int count)
    {
        switch (Kind)
        {
            case WindowKind.Chest:
            {
                var chest = BlockEntityManager.GetOrCreateChest(BlockPosition);
                for (int i = 0; i < count; i++) chest.SetSlot(i, slots[i]);
                break;
            }

            case WindowKind.DoubleChest:
            {
                var chest = BlockEntityManager.GetOrCreateDoubleChest(BlockPosition);
                for (int i = 0; i < count; i++) chest.SetSlot(i, slots[i]);
                break;
            }

            case WindowKind.Furnace:
            {
                var furnace = BlockEntityManager.GetOrCreateFurnace(BlockPosition);
                furnace.InputSlot = slots[0];
                furnace.FuelSlot = slots[1];
                furnace.OutputSlot = slots[2];
                break;
            }

            case WindowKind.Workbench:
            case WindowKind.PlayerInventory:
            {
                // Slot 0 is the result, which is computed - never written back from a click.
                var grid = Grid!;
                for (int i = 0; i < grid.Slots.Length; i++) grid.SetSlot(i, slots[1 + i]);
                break;
            }
        }
    }
}

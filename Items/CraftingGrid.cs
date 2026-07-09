// Main class that handles crafting grid logic. | DA | 3/5/26
using VoxelEngine.GameEntity;

namespace VoxelEngine.Items;

/// <summary>
/// Backing model for a crafting UI's input grid (used by both the 2x2 inventory grid and the 3x3 workbench grid). Holds the raw slot contents plus the currently matched recipe's result (if any), recomputing the match any time a slot changes.
/// </summary>
public class CraftingGrid
{
    public readonly int Width;
    public readonly int Height;
    public readonly ItemStack?[] Slots;

    /// <summary>The output preview shown in the result slot; null when no recipe currently matches the grid.</summary>
    public ItemStack? Result { get; private set; }

    public CraftingGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Slots = new ItemStack?[width * height];
    }

    public ItemStack? GetSlot(int index) => Slots[index];

    /// <summary>Places/replaces the stack in a slot and re-evaluates the recipe match.</summary>
    public void SetSlot(int index, ItemStack? stack)
    {
        Slots[index] = stack;
        RefreshResult();
    }

    private void RefreshResult()
    {
        Result = CraftingRegistry.FindMatch(Slots, Width, Height)?.Result;
    }

    /// <summary>
    /// Consumes one item from every occupied ingredient slot (decrementing count, clearing slots that drop to 0) and returns the crafted result. Called when the player takes the crafted item out of the result slot. Returns null if no recipe currently matches.
    /// </summary>
    public ItemStack? TakeResult()
    {
        var recipe = CraftingRegistry.FindMatch(Slots, Width, Height);

        if (recipe == null)
            return null;

        var crafted = recipe.Result;

        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] == null)
                continue;

            var s = Slots[i]!.Value;
            Slots[i] = s.Count <= 1 ? null : s.WithCount(s.Count - 1);
        }

        RefreshResult();
        return crafted;
    }

    /// <summary>
    /// Dumps every remaining ingredient back into the player's inventory and clears the grid. Used when a crafting UI (e.g. workbench) is closed with leftover ingredients still in it.
    /// </summary>
    public void ReturnItemsTo(PlayerInventory inv)
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] == null)
                continue;

            inv.TryAdd(Slots[i]!.Value);
            Slots[i] = null;
        }

        Result = null;
    }
}
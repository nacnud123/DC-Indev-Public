// Main class that handles crafting grid logic. | DA | 3/5/26
using VoxelEngine.GameEntity;

namespace VoxelEngine.Items;

public class CraftingGrid
{
    public readonly int Width;
    public readonly int Height;
    public readonly ItemStack?[] Slots;
    public ItemStack? Result { get; private set; }

    public CraftingGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Slots = new ItemStack?[width * height];
    }

    public ItemStack? GetSlot(int index) => Slots[index];

    public void SetSlot(int index, ItemStack? stack)
    {
        Slots[index] = stack;
        RefreshResult();
    }

    private void RefreshResult()
    {
        Result = CraftingRegistry.FindMatch(Slots, Width, Height)?.Result;
    }

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
// Holds reference to smelting recipes and fuel values. | DA | 3/5/26
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

/// <summary>One furnace recipe: an input stack that smelts into an output stack over a fixed number of ticks.</summary>
public class SmeltRecipe
{
    public ItemStack Input;
    public ItemStack Output;

    /// <summary>Game ticks required to fully smelt one input into one output (default 200, matching Minecraft-style furnace timing).</summary>
    public int TicksToSmelt;

    public SmeltRecipe(ItemStack input, ItemStack output, int ticksToSmelt = 200)
    {
        Input        = input;
        Output       = output;
        TicksToSmelt = ticksToSmelt;
    }
}

/// <summary>
/// Static registry of furnace smelting recipes and fuel burn-time values. Recipes are matched against a furnace's input slot by stack equality (item/block type only, per <see cref="ItemStack.Equals"/>). Fuel values are keyed by the block/item's enum name string rather than the type itself so both BlockType and ItemType fuels share one dictionary.
/// </summary>
public static class SmeltRegistry
{
    private static readonly List<SmeltRecipe> Recipes = new();
    private static readonly Dictionary<string, int> FuelValues = new();

    static SmeltRegistry()
    {
        RegisterRecipes();
        RegisterFuels();
    }

    /// <summary>Finds the smelt recipe whose input matches the given stack (type-only comparison), or null if none.</summary>
    public static SmeltRecipe? FindMatch(ItemStack? input)
    {
        if (!input.HasValue) return null;
        foreach (var recipe in Recipes)
            if (recipe.Input == input.Value) return recipe;
        return null;
    }

    /// <summary>Looks up how many smelt-ticks worth of burn time a given fuel stack provides; 0 if it isn't a valid fuel.</summary>
    public static int GetFuelValue(ItemStack? fuel)
    {
        if (!fuel.HasValue) return 0;
        var key = fuel.Value.IsBlock ? fuel.Value.Block.ToString() : fuel.Value.Item.ToString();
        return FuelValues.GetValueOrDefault(key, 0);
    }

    // Registers every ore-smelting, glass-making, stone-cooking, and food-cooking recipe.
    private static void RegisterRecipes()
    {
        ItemStack B(BlockType b) => ItemStack.FromBlock(b);
        ItemStack I(ItemType t)  => ItemStack.FromItem(t);

        Recipes.Add(new SmeltRecipe(B(BlockType.IronOre),     I(ItemType.IronBar)));
        Recipes.Add(new SmeltRecipe(B(BlockType.GoldOre),     I(ItemType.GoldBar)));
        Recipes.Add(new SmeltRecipe(B(BlockType.CoalOre),     I(ItemType.Coal)));
        Recipes.Add(new SmeltRecipe(B(BlockType.DiamondOre),  I(ItemType.Diamond)));
        Recipes.Add(new SmeltRecipe(B(BlockType.Sand),        B(BlockType.Glass)));
        Recipes.Add(new SmeltRecipe(B(BlockType.CobbleStone), B(BlockType.Stone)));
        Recipes.Add(new SmeltRecipe(B(BlockType.Clay),        B(BlockType.Bricks)));
        Recipes.Add(new SmeltRecipe(I(ItemType.RawPork),      I(ItemType.CookedPork)));
    }

    // Registers fuel burn durations, in smelt-ticks. Coal burns longest; plank/leaf-derived fuels are shorter, and wood-family blocks that aren't obviously fuel (workbench, chest, stairs) still burn since they're made of wood.
    private static void RegisterFuels()
    {
        void Fuel(string key, int ticks) => FuelValues[key] = ticks;

        Fuel(ItemType.Coal.ToString(),         1600);
        Fuel(BlockType.Wood.ToString(),         300);
        Fuel(BlockType.Planks.ToString(),       150);
        Fuel(BlockType.WorkBench.ToString(),    300);
        Fuel(BlockType.WoodSlab.ToString(),      75);
        Fuel(BlockType.WoodenStair.ToString(),  300);
        Fuel(BlockType.Chest.ToString(),        300);
        Fuel(BlockType.Leaves.ToString(),        50);
        Fuel(ItemType.Stick.ToString(),          50);
    }
}

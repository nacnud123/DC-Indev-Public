// Holds reference to smelting recipes and fuel values. | DA | 3/5/26
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

public class SmeltRecipe
{
    public ItemStack Input;
    public ItemStack Output;
    public int TicksToSmelt;

    public SmeltRecipe(ItemStack input, ItemStack output, int ticksToSmelt = 200)
    {
        Input        = input;
        Output       = output;
        TicksToSmelt = ticksToSmelt;
    }
}

public static class SmeltRegistry
{
    private static readonly List<SmeltRecipe> Recipes = new();
    private static readonly Dictionary<string, int> FuelValues = new();

    static SmeltRegistry()
    {
        RegisterRecipes();
        RegisterFuels();
    }

    public static SmeltRecipe? FindMatch(ItemStack? input)
    {
        if (!input.HasValue) return null;
        foreach (var recipe in Recipes)
            if (recipe.Input == input.Value) return recipe;
        return null;
    }

    public static int GetFuelValue(ItemStack? fuel)
    {
        if (!fuel.HasValue) return 0;
        var key = fuel.Value.IsBlock ? fuel.Value.Block.ToString() : fuel.Value.Item.ToString();
        return FuelValues.GetValueOrDefault(key, 0);
    }

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

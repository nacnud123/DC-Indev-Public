// Main class used to register and manage crafting recipes for the game. | DA | 3/5/26
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

public static class CraftingRegistry
{
    private static readonly List<CraftingRecipe> Recipes = new();

    static CraftingRegistry() => RegisterAll();

    public static void Register(CraftingRecipe recipe) => Recipes.Add(recipe);

    public static CraftingRecipe? FindMatch(ItemStack?[] grid, int gridWidth, int gridHeight)
    {
        foreach (var recipe in Recipes)
        {
            bool matched = recipe.IsShaped
                ? MatchShaped(recipe, grid, gridWidth, gridHeight)
                : MatchShapeless(recipe, grid);

            if (matched) return recipe;
        }

        return null;
    }
    
    private static bool MatchShaped(CraftingRecipe recipe, ItemStack?[] grid, int gridWidth, int gridHeight)
    {
        int minRow = gridHeight, maxRow = -1;
        int minCol = gridWidth, maxCol = -1;

        for (int r = 0; r < gridHeight; r++)
        {
            for (int c = 0; c < gridWidth; c++)
            {
                if (grid[r * gridWidth + c] == null)
                    continue;

                if (r < minRow)
                    minRow = r;

                if (r > maxRow)
                    maxRow = r;

                if (c < minCol)
                    minCol = c;

                if (c > maxCol)
                    maxCol = c;
            }
        }

        if (maxRow == -1)
            return false; // empty grid

        int contentWidth = maxCol - minCol + 1;
        int contentHeight = maxRow - minRow + 1;

        if (contentWidth != recipe.Width || contentHeight != recipe.Height)
            return false;

        return ComparePattern(recipe, grid, gridWidth, minRow, minCol, false)
            || (recipe.Width > 1 && ComparePattern(recipe, grid, gridWidth, minRow, minCol, true));
    }

    private static bool ComparePattern(CraftingRecipe recipe, ItemStack?[] grid, int gridWidth, int minRow, int minCol, bool mirrorH)
    {
        for (int r = 0; r < recipe.Height; r++)
        {
            for (int c = 0; c < recipe.Width; c++)
            {
                int recipeCol = mirrorH ? (recipe.Width - 1 - c) : c;
                var gridCell = grid[(minRow + r) * gridWidth + (minCol + c)];
                var recipeCell = recipe.Ingredients[r * recipe.Width + recipeCol];

                if (recipeCell == null && gridCell != null)
                    return false;

                if (recipeCell != null && gridCell == null)
                    return false;

                if (recipeCell != null && recipeCell.Value != gridCell!.Value)
                    return false;
            }
        }

        return true;
    }

    private static bool MatchShapeless(CraftingRecipe recipe, ItemStack?[] grid)
    {
        var gridCounts = CountItems(grid);
        var recipeCounts = CountItems(recipe.Ingredients);

        if (gridCounts.Count != recipeCounts.Count)
            return false;

        foreach (var (item, count) in recipeCounts)
        {
            if (!gridCounts.TryGetValue(item, out int gridCount) || gridCount != count)
                return false;
        }

        return true;
    }

    private static Dictionary<ItemStack, int> CountItems(ItemStack?[] slots)
    {
        var counts = new Dictionary<ItemStack, int>();
        foreach (var slot in slots)
        {
            if (slot == null)
                continue;

            counts.TryGetValue(slot.Value, out int n);
            counts[slot.Value] = n + 1;
        }

        return counts;
    }

    private static void RegisterAll()
    {
        // Short aliases so pattern arrays read as visual grids below.
        ItemStack B(BlockType b, int n = 1) => ItemStack.FromBlock(b, n);
        ItemStack I(ItemType t, int n = 1) => ItemStack.FromItem(t, n);

        var W = B(BlockType.Wood);
        var P = B(BlockType.Planks);
        var C = B(BlockType.CobbleStone);
        var S = I(ItemType.Stick);
        var Di = I(ItemType.Diamond);
        var Fe = I(ItemType.IronBar);
        var Au = I(ItemType.GoldBar);
        var St = I(ItemType.String);
        var Wo = B(BlockType.White);

        // Resources
        Register(CraftingRecipe.Shaped(B(BlockType.Planks, 4), 1, 1, [W]));

        Register(CraftingRecipe.Shaped(I(ItemType.Stick, 4), 1, 2, [
            P,
            P
        ]));

        Register(CraftingRecipe.Shaped(B(BlockType.WorkBench), 2, 2, [
            P, P,
            P, P
        ]));

        Register(CraftingRecipe.Shaped(B(BlockType.Chest), 3, 3, [
            P, P, P,
            P, null, P,
            P, P, P
        ]));

        Register(CraftingRecipe.Shaped(B(BlockType.Furnace), 3, 3, [
            C, C, C,
            C, null, C,
            C, C, C
        ]));

        // Slabs
        Register(CraftingRecipe.Shaped(B(BlockType.Stoneslab, 6), 3, 1, [C, C, C]));
        Register(CraftingRecipe.Shaped(B(BlockType.WoodSlab, 6), 3, 1, [P, P, P]));

        // Stairs
        Register(CraftingRecipe.Shaped(B(BlockType.WoodenStair), 3, 3, [
            P, null, null,
            P, P, null,
            P, P, P
        ]));
        Register(CraftingRecipe.Shaped(B(BlockType.StoneStair), 3, 3, [
            C, null, null,
            C, C, null,
            C, C, C
        ]));

        // Wood tools
        Register(CraftingRecipe.Shaped(I(ItemType.WoodPickaxe), 3, 3, [
            P, P, P,
            null, S, null,
            null, S, null
        ]));

        Register(CraftingRecipe.Shaped(I(ItemType.WoodSword), 1, 3, [
            P,
            P,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.WoodAxe), 2, 3, [
            P, P,
            P, S,
            null, S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.WoodShovel), 1, 3, [
            P,
            S,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.WoodHoe), 2, 3, [
            P, P,
            null, S,
            null, S
        ]));

        // Stone tools
        Register(CraftingRecipe.Shaped(I(ItemType.StonePickaxe), 3, 3, [
            C, C, C,
            null, S, null,
            null, S, null
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.StoneSword), 1, 3, [
            C,
            C,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.StoneAxe), 2, 3, [
            C, C,
            C, S,
            null, S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.StoneShovel), 1, 3, [
            C,
            S,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.StoneHoe), 2, 3, [
            C, C,
            null, S,
            null, S
        ]));

        // Iron tools
        Register(CraftingRecipe.Shaped(I(ItemType.IronPickaxe), 3, 3, [
            Fe, Fe, Fe,
            null, S, null,
            null, S, null
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronSword), 1, 3, [
            Fe,
            Fe,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronAxe), 2, 3, [
            Fe, Fe,
            Fe, S,
            null, S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronShovel), 1, 3, [
            Fe,
            S,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronHoe), 2, 3, [
            Fe, Fe,
            null, S,
            null, S
        ]));

        // Gold tools
        Register(CraftingRecipe.Shaped(I(ItemType.GoldPickaxe), 3, 3, [
            Au, Au, Au,
            null, S, null,
            null, S, null
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.GoldSword), 1, 3, [
            Au,
            Au,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.GoldAxe), 2, 3, [
            Au, Au,
            Au, S,
            null, S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.GoldShovel), 1, 3, [
            Au,
            S,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.GoldHoe), 2, 3, [
            Au, Au,
            null, S,
            null, S
        ]));

        // Diamond tools
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondPickaxe), 3, 3, [
            Di, Di, Di,
            null, S, null,
            null, S, null
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondSword), 1, 3, [
            Di,
            Di,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondAxe), 2, 3, [
            Di, Di,
            Di, S,
            null, S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondShovel), 1, 3, [
            Di,
            S,
            S
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondHoe), 2, 3, [
            Di, Di,
            null, S,
            null, S
        ]));

        // Iron armor
        Register(CraftingRecipe.Shaped(I(ItemType.IronHelmet), 3, 2, [
            Fe, Fe, Fe,
            Fe, null, Fe
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronChest), 3, 3, [
            Fe, null, Fe,
            Fe, Fe, Fe,
            Fe, Fe, Fe
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronLegs), 3, 3, [
            Fe, Fe, Fe,
            Fe, null, Fe,
            Fe, null, Fe
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.IronBoots), 3, 2, [
            Fe, null, Fe,
            Fe, null, Fe
        ]));

        // Gold armor
        Register(CraftingRecipe.Shaped(I(ItemType.GoldHelmet), 3, 2, [
            Au, Au, Au,
            Au, null, Au
        ]));

        Register(CraftingRecipe.Shaped(I(ItemType.GoldChest), 3, 3, [
            Au, null, Au,
            Au, Au, Au,
            Au, Au, Au
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.GoldLegs), 3, 3, [
            Au, Au, Au,
            Au, null, Au,
            Au, null, Au
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.GoldBoots), 3, 2, [
            Au, null, Au,
            Au, null, Au
        ]));

        // Diamond armor
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondHelmet), 3, 2, [
            Di, Di, Di,
            Di, null, Di
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondChest), 3, 3, [
            Di, null, Di,
            Di, Di, Di,
            Di, Di, Di
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondLegs), 3, 3, [
            Di, Di, Di,
            Di, null, Di,
            Di, null, Di
        ]));
        Register(CraftingRecipe.Shaped(I(ItemType.DiamondBoots), 3, 2, [
            Di, null, Di,
            Di, null, Di
        ]));

        // Torches
        Register(CraftingRecipe.Shaped(B(BlockType.Torch, 4), 1, 2, [
            I(ItemType.Coal),
            S
        ]));

        // Bowl
        Register(CraftingRecipe.Shaped(I(ItemType.EmptyBowl), 3, 2, [
            P, null, P,
            null, P, null
        ]));

        // Food
        Register(CraftingRecipe.Shaped(I(ItemType.Bread), 3, 1,
            [I(ItemType.Wheat), I(ItemType.Wheat), I(ItemType.Wheat)]));
        Register(CraftingRecipe.Shapeless(I(ItemType.Stew), [
            B(BlockType.BrownMushroom), B(BlockType.RedMushroom), I(ItemType.EmptyBowl)
        ]));

        // Ranged
        Register(CraftingRecipe.Shaped(I(ItemType.Bow), 3, 3, [
            St, S, null,
            St, null, S,
            St, S, null
        ]));
        // Arrow
        Register(CraftingRecipe.Shaped(I(ItemType.Arrow, 4), 1, 3, [
            I(ItemType.Flint),
            S,
            I(ItemType.Feather)
        ]));

        // Painting
        Register(CraftingRecipe.Shaped(I(ItemType.Painting), 3, 3, [
            S, S, S,
            S, Wo, S,
            S, S, S
        ]));
    }
}
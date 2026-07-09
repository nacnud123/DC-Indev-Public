// Main class that used to define what a crafting recipe is. | DA | 3/5/26
namespace VoxelEngine.Items;

/// <summary>
/// Defines a single crafting recipe: either a shaped pattern (ingredient positions matter, e.g. tools/stairs) or a shapeless bag of ingredients (position doesn't matter, e.g. stew). Instances are built via the static factory methods and matched against a live <see cref="CraftingGrid"/> by <see cref="CraftingRegistry"/>.
/// </summary>
public class CraftingRecipe
{
    public readonly ItemStack?[] Ingredients; // row-major, null = empty slot
    public readonly int Width;
    public readonly int Height;

    /// <summary>True = ingredient positions must match the grid pattern (allowing horizontal mirroring); false = shapeless (only ingredient counts matter).</summary>
    public readonly bool IsShaped;
    public readonly ItemStack Result;

    private CraftingRecipe(ItemStack result, int width, int height, bool shaped, ItemStack?[] ingredients)
    {
        Result = result;
        Width = width;
        Height = height;
        IsShaped = shaped;
        Ingredients = ingredients;
    }

    /// <summary>
    /// Builds a shaped recipe from a row-major pattern array. The pattern's bounding box (not the full grid) is what gets compared against the crafting grid's occupied cells, so a 1x2 pattern still matches wherever it's placed within a larger grid.
    /// </summary>
    public static CraftingRecipe Shaped(ItemStack result, int width, int height, ItemStack?[] pattern)
    {
        if (pattern.Length != width * height)
            throw new ArgumentException($"Pattern length {pattern.Length} does not match {width}x{height}={width * height}");

        return new CraftingRecipe(result, width, height, true, pattern);
    }

    /// <summary>
    /// Builds a shapeless recipe: matches any grid containing exactly these ingredients (by type and count), regardless of which slots they occupy.
    /// </summary>
    public static CraftingRecipe Shapeless(ItemStack result, ItemStack[] ingredients)
    {
        var nullable = new ItemStack?[ingredients.Length];
        for (int i = 0; i < ingredients.Length; i++)
            nullable[i] = ingredients[i];

        return new CraftingRecipe(result, ingredients.Length, 1, false, nullable);
    }
}

// Main class that used to define what a crafting recipe is. | DA | 3/5/26
namespace VoxelEngine.Items;

public class CraftingRecipe
{
    public readonly ItemStack?[] Ingredients; // row-major, null = empty slot
    public readonly int Width;
    public readonly int Height;
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

    public static CraftingRecipe Shaped(ItemStack result, int width, int height, ItemStack?[] pattern)
    {
        if (pattern.Length != width * height)
            throw new ArgumentException($"Pattern length {pattern.Length} does not match {width}x{height}={width * height}");

        return new CraftingRecipe(result, width, height, true, pattern);
    }

    public static CraftingRecipe Shapeless(ItemStack result, ItemStack[] ingredients)
    {
        var nullable = new ItemStack?[ingredients.Length];
        for (int i = 0; i < ingredients.Length; i++)
            nullable[i] = ingredients[i];

        return new CraftingRecipe(result, ingredients.Length, 1, false, nullable);
    }
}

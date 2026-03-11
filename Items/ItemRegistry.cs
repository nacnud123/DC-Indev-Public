// Registry that holds one Item instance per ItemType | DA | 3/8/26
using System.Linq;
using VoxelEngine.Rendering;

namespace VoxelEngine.Items;

public static class ItemRegistry
{
    private static readonly Dictionary<ItemType, Item> Items = new();

    static ItemRegistry()
    {
        // Tools — Wood
        Register(new ItemWoodPickaxe(), new ItemWoodSword(), new ItemWoodAxe(), new ItemWoodShovel(), new ItemWoodHoe());

        // Tools — Stone
        Register(new ItemStonePickaxe(), new ItemStoneSword(), new ItemStoneAxe(), new ItemStoneShovel(), new ItemStoneHoe());

        // Tools — Iron
        Register(new ItemIronPickaxe(), new ItemIronSword(), new ItemIronAxe(), new ItemIronShovel(), new ItemIronHoe());

        // Tools — Gold
        Register(new ItemGoldPickaxe(), new ItemGoldSword(), new ItemGoldAxe(), new ItemGoldShovel(), new ItemGoldHoe());

        // Tools — Diamond
        Register(new ItemDiamondPickaxe(), new ItemDiamondSword(), new ItemDiamondAxe(), new ItemDiamondShovel(), new ItemDiamondHoe());

        // Special tools
        Register(new ItemFlintSteel(), new ItemBow());

        // Resources
        Register(new ItemStick(), new ItemDiamond(), new ItemCoal(), new ItemIronBar(), new ItemGoldBar(),
            new ItemSulfur(), new ItemFlint(), new ItemBone(), new ItemFeather(), new ItemString(),
            new ItemWheat(), new ItemEmptyBowl(), new ItemArrow());

        // Food
        Register(new ItemApple(), new ItemRawPork(), new ItemCookedPork(), new ItemBread(), new ItemStew(), new ItemSeeds());

        // Decorative
        Register(new ItemPainting());

        // Armor — Leather
        Register(new ItemLeatherHelmet(), new ItemLeatherChest(), new ItemLeatherLegs(), new ItemLeatherBoots());

        // Armor — Iron
        Register(new ItemIronHelmet(), new ItemIronChest(), new ItemIronLegs(), new ItemIronBoots());

        // Armor — Gold
        Register(new ItemGoldHelmet(), new ItemGoldChest(), new ItemGoldLegs(), new ItemGoldBoots());

        // Armor — Diamond
        Register(new ItemDiamondHelmet(), new ItemDiamondChest(), new ItemDiamondLegs(), new ItemDiamondBoots());
    }

    private static void Register(params Item[] items)
    {
        foreach (var item in items)
            Items[item.Type] = item;
    }

    public static Item Get(ItemType type)
    {
        if (Items.TryGetValue(type, out var item))
            return item;

        throw new ArgumentException($"Unknown item type: {type}");
    }

    public static IEnumerable<(ItemType, Item)> GetAll() => Items.Select(kv => (kv.Key, kv.Value));

    public static string GetName(ItemType type) => Get(type).Name;
    public static TextureCoords GetItemCoords(ItemType type) => Get(type).ItemCoords;
}

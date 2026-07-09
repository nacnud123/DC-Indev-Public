// Registry that holds one Item instance per ItemType | DA | 3/8/26
using System.Linq;
using VoxelEngine.Rendering;

namespace VoxelEngine.Items;

/// <summary>
/// Static singleton registry mapping each <see cref="ItemType"/> to its single shared <see cref="Item"/> instance (mirrors BlockRegistry for blocks). All item definitions are instantiated once in the static constructor and looked up by type thereafter — items are stateless definitions, with per-slot state (count, durability) tracked separately in <see cref="ItemStack"/>.
/// </summary>
public static class ItemRegistry
{
    private static readonly Dictionary<ItemType, Item> Items = new();

    // Registers every concrete Item subclass, grouped by category for readability.
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

    /// <summary>Adds one or more item instances to the registry, keyed by their own Type property.</summary>
    private static void Register(params Item[] items)
    {
        foreach (var item in items)
            Items[item.Type] = item;
    }

    /// <summary>Looks up the shared Item instance for a given type; throws if the type was never registered.</summary>
    public static Item Get(ItemType type)
    {
        if (Items.TryGetValue(type, out var item))
            return item;

        throw new ArgumentException($"Unknown item type: {type}");
    }

    /// <summary>Enumerates every registered (type, item) pair, e.g. for populating creative-mode UIs.</summary>
    public static IEnumerable<(ItemType, Item)> GetAll() => Items.Select(kv => (kv.Key, kv.Value));

    public static string GetName(ItemType type) => Get(type).Name;
    public static TextureCoords GetItemCoords(ItemType type) => Get(type).ItemCoords;
}

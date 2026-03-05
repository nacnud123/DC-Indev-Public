// Main class that holds item definitions and provides utility methods for working with items. | DA | 3/5/26
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

// OnUse: called on right-click. Returns true if the item was consumed/used successfully.
public delegate bool ItemUseAction(World world, OpenTK.Mathematics.Vector3i blockPos, OpenTK.Mathematics.Vector3i? placePos);

public record ItemDef(
    string Name,
    TextureCoords ItemCoords,
    ToolType ToolType = ToolType.None,
    ToolTier ToolTier = ToolTier.None,
    int MaxDurability = -1,
    float MiningSpeed = 1f,
    int AttackDamage = 1,
    bool IsFood = false,
    int FoodRestore = 0,
    int MaxStackSize = 1,
    ItemUseAction? OnUse = null,
    ArmorSlot? ArmorSlot = null,
    ArmorTier? ArmorTier = null,
    int ArmorPoints = 0,
    bool SkipBlockRaycast = false // true for items that fire/use without needing a block target
)
{
    public bool IsTool => ToolType != ToolType.None;
    public bool IsArmor => ArmorSlot.HasValue;
}

public static class ItemRegistry
{
    private static readonly Dictionary<ItemType, ItemDef> Items = new();

    static ItemRegistry()
    {
        // Tools — Wood
        Register(ItemType.WoodPickaxe, "Wooden Pickaxe", (0, 7), ToolType.Pickaxe, ToolTier.Wood, 60, 2f, 2);
        Register(ItemType.WoodSword, "Wooden Sword", (1, 7), ToolType.Sword, ToolTier.Wood, 60, 1f, 4);
        Register(ItemType.WoodAxe, "Wooden Axe", (2, 7), ToolType.Axe, ToolTier.Wood, 60, 2f, 3);
        Register(ItemType.WoodShovel, "Wooden Shovel", (3, 7), ToolType.Shovel, ToolTier.Wood, 60, 2f, 1);
        Register(ItemType.WoodHoe, new ItemDef("Wooden Hoe", UvHelper.FromTileCoords(4, 7), ToolType.Hoe, ToolTier.Wood,
            60, 1f, 1,
            OnUse: (world, blockPos, placePos) =>
            {
                var b = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
                if (b != BlockType.Grass && b != BlockType.Dirt)
                    return false;
                
                if (world.GetBlock(blockPos.X, blockPos.Y + 1, blockPos.Z) != BlockType.Air)
                    return false;
                
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Farmland);
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                return true;
            }));

        // Tools — Stone
        Register(ItemType.StonePickaxe, "Stone Pickaxe", (0, 6), ToolType.Pickaxe, ToolTier.Stone, 132, 4f, 3);
        Register(ItemType.StoneSword, "Stone Sword", (1, 6), ToolType.Sword, ToolTier.Stone, 132, 1f, 5);
        Register(ItemType.StoneAxe, "Stone Axe", (2, 6), ToolType.Axe, ToolTier.Stone, 132, 4f, 4);
        Register(ItemType.StoneShovel, "Stone Shovel", (3, 6), ToolType.Shovel, ToolTier.Stone, 132, 4f, 2);
        Register(ItemType.StoneHoe, new ItemDef("Stone Hoe", UvHelper.FromTileCoords(4, 6), ToolType.Hoe,
            ToolTier.Stone, 132, 1f, 1,
            OnUse: (world, blockPos, placePos) =>
            {
                var b = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
                if (b != BlockType.Grass && b != BlockType.Dirt)
                    return false;
                
                if (world.GetBlock(blockPos.X, blockPos.Y + 1, blockPos.Z) != BlockType.Air)
                    return false;
                
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Farmland);
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                return true;
            }));

        // Tools — Iron
        Register(ItemType.IronPickaxe, "Iron Pickaxe", (0, 5), ToolType.Pickaxe, ToolTier.Iron, 251, 6f, 4);
        Register(ItemType.IronSword, "Iron Sword", (1, 5), ToolType.Sword, ToolTier.Iron, 251, 1f, 6);
        Register(ItemType.IronAxe, "Iron Axe", (2, 5), ToolType.Axe, ToolTier.Iron, 251, 6f, 5);
        Register(ItemType.IronShovel, "Iron Shovel", (3, 5), ToolType.Shovel, ToolTier.Iron, 251, 6f, 3);
        Register(ItemType.IronHoe, new ItemDef("Iron Hoe", UvHelper.FromTileCoords(4, 5), ToolType.Hoe, ToolTier.Iron,
            251, 1f, 1,
            OnUse: (world, blockPos, placePos) =>
            {
                var b = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
                if (b != BlockType.Grass && b != BlockType.Dirt)
                    return false;
                
                if (world.GetBlock(blockPos.X, blockPos.Y + 1, blockPos.Z) != BlockType.Air)
                    return false;
                
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Farmland);
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                return true;
            }));

        // Tools — Gold
        Register(ItemType.GoldPickaxe, "Gold Pickaxe", (0, 4), ToolType.Pickaxe, ToolTier.Gold, 33, 12f, 2);
        Register(ItemType.GoldSword, "Gold Sword", (1, 4), ToolType.Sword, ToolTier.Gold, 33, 1f, 4);
        Register(ItemType.GoldAxe, "Gold Axe", (2, 4), ToolType.Axe, ToolTier.Gold, 33, 12f, 3);
        Register(ItemType.GoldShovel, "Gold Shovel", (3, 4), ToolType.Shovel, ToolTier.Gold, 33, 12f, 1);
        Register(ItemType.GoldHoe, new ItemDef("Gold Hoe", UvHelper.FromTileCoords(4, 4), ToolType.Hoe, ToolTier.Gold,
            33, 1f, 1,
            OnUse: (world, blockPos, placePos) =>
            {
                var b = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
                if (b != BlockType.Grass && b != BlockType.Dirt)
                    return false;
                
                if (world.GetBlock(blockPos.X, blockPos.Y + 1, blockPos.Z) != BlockType.Air)
                    return false;
                
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Farmland);
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                return true;
            }));

        // Tools — Diamond
        Register(ItemType.DiamondPickaxe, "Diamond Pickaxe", (0, 3), ToolType.Pickaxe, ToolTier.Diamond, 1562, 8f, 5);
        Register(ItemType.DiamondSword, "Diamond Sword", (1, 3), ToolType.Sword, ToolTier.Diamond, 1562, 1f, 7);
        Register(ItemType.DiamondAxe, "Diamond Axe", (2, 3), ToolType.Axe, ToolTier.Diamond, 1562, 8f, 6);
        Register(ItemType.DiamondShovel, "Diamond Shovel", (3, 3), ToolType.Shovel, ToolTier.Diamond, 1562, 8f, 4);
        Register(ItemType.DiamondHoe, new ItemDef("Diamond Hoe", UvHelper.FromTileCoords(4, 3), ToolType.Hoe,
            ToolTier.Diamond, 1562, 1f, 1,
            OnUse: (world, blockPos, placePos) =>
            {
                var b = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
                if (b != BlockType.Grass && b != BlockType.Dirt)
                    return false;
                
                if (world.GetBlock(blockPos.X, blockPos.Y + 1, blockPos.Z) != BlockType.Air)
                    return false;
                
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Farmland);
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                
                return true;
            }));

        // Misc
        Register(ItemType.FlintSteel, new ItemDef("Flint & Steel", UvHelper.FromTileCoords(5, 7),
            ToolType: ToolType.Misc, MaxDurability: 64,
            OnUse: (world, blockPos, placePos) =>
            {
                if (!placePos.HasValue)
                    return false;

                int fx = placePos.Value.X, fy = placePos.Value.Y, fz = placePos.Value.Z;

                if (world.GetBlock(fx, fy, fz) != BlockType.Air)
                    return false;

                world.SetBlock(fx, fy, fz, BlockType.Fire);
                world.SetChunkAsModified(fx, fy, fz);
                return true;
            }));

        // Food
        Register(ItemType.Apple, new ItemDef("Apple", UvHelper.FromTileCoords(7, 5), IsFood: true, FoodRestore: 4, MaxStackSize: 64));
        Register(ItemType.RawPork, new ItemDef("Raw Pork", UvHelper.FromTileCoords(7, 7), IsFood: true, FoodRestore: 3, MaxStackSize: 64));
        Register(ItemType.CookedPork, new ItemDef("Cooked Pork", UvHelper.FromTileCoords(7, 6), IsFood: true, FoodRestore: 8, MaxStackSize: 64));

        // Items
        Register(ItemType.Stick, new ItemDef("Stick", UvHelper.FromTileCoords(0, 0), MaxStackSize: 64));

        // Resources
        Register(ItemType.Diamond, new ItemDef("Diamond", UvHelper.FromTileCoords(0, 1), MaxStackSize: 64));
        Register(ItemType.Coal, new ItemDef("Coal", UvHelper.FromTileCoords(1, 1), MaxStackSize: 64));
        Register(ItemType.IronBar, new ItemDef("Iron Bar", UvHelper.FromTileCoords(2, 1), MaxStackSize: 64));
        Register(ItemType.GoldBar, new ItemDef("Gold Bar", UvHelper.FromTileCoords(3, 1), MaxStackSize: 64));
        Register(ItemType.Sulfur, new ItemDef("Sulfur", UvHelper.FromTileCoords(4, 1), MaxStackSize: 64));
        Register(ItemType.Flint, new ItemDef("Flint", UvHelper.FromTileCoords(5, 1), MaxStackSize: 64));
        Register(ItemType.Bone, new ItemDef("Bone", UvHelper.FromTileCoords(5, 6), MaxStackSize: 64));
        Register(ItemType.Feather, new ItemDef("Feather", UvHelper.FromTileCoords(5, 5), MaxStackSize: 64));
        Register(ItemType.String, new ItemDef("String", UvHelper.FromTileCoords(5, 4), MaxStackSize: 64));

        // Decorative / misc
        Register(ItemType.Painting, new ItemDef("Painting", UvHelper.FromTileCoords(5, 3), MaxStackSize: 64,
            OnUse: (world, blockPos, placePos) =>
            {
                if (!placePos.HasValue)
                    return false;

                // Derive facing from which face was clicked (placePos is in front of wall)
                var diff = placePos.Value - blockPos;
                byte facing;
                if (diff.Z == -1) 
                    facing = 0; // North
                else if (diff.Z == 1) 
                    facing = 1; // South
                else if (diff.X == 1) 
                    facing = 2; // East
                else if (diff.X == -1) 
                    facing = 3; // West
                else return false; // top/bottom face — can't place painting

                var candidates = new List<PaintingEntity>();
                foreach (var def in PaintingRegistry.All)
                {
                    var candidate = new PaintingEntity(blockPos, facing, def);
                    
                    if (candidate.IsValidSurface(world))
                        candidates.Add(candidate);
                }

                if (candidates.Count == 0)
                    return false;

                var chosen = candidates[Game.Instance.GameRandom.Next(candidates.Count)];
                world.AddEntity(chosen);
                return true;
            }));

        // Food & farming
        Register(ItemType.Bread,
            new ItemDef("Bread", UvHelper.FromTileCoords(7, 4), IsFood: true, FoodRestore: 5, MaxStackSize: 64));
        Register(ItemType.Wheat, new ItemDef("Wheat", UvHelper.FromTileCoords(7, 3), MaxStackSize: 64));
        Register(ItemType.EmptyBowl, new ItemDef("Empty Bowl", UvHelper.FromTileCoords(7, 2), MaxStackSize: 64));
        Register(ItemType.Stew,
            new ItemDef("Mushroom Stew", UvHelper.FromTileCoords(8, 2), IsFood: true, FoodRestore: 6));
        Register(ItemType.Seeds, new ItemDef("Seeds", UvHelper.FromTileCoords(8, 7), MaxStackSize: 64,
            OnUse: (world, blockPos, placePos) =>
            {
                if (world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z) != BlockType.Farmland)
                    return false;

                int px = blockPos.X, py = blockPos.Y + 1, pz = blockPos.Z;
                if (world.GetBlock(px, py, pz) != BlockType.Air)
                    return false;

                world.SetBlock(px, py, pz, BlockType.WheatStage0);
                world.SetChunkAsModified(px, py, pz);
                return true;
            }));

        // Ranged
        Register(ItemType.Bow, new ItemDef("Bow", UvHelper.FromTileCoords(0, 2),
            ToolType: ToolType.Bow, MaxDurability: 384, SkipBlockRaycast: true,
            OnUse: (world, blockPos, placePos) =>
            {
                var inv = Game.Instance.PlayerInventory;
                var player = Game.Instance.GetPlayer;
                
                if (inv == null) 
                    return false;

                int arrowSlot = inv.FindItem(ItemType.Arrow);
                
                if (arrowSlot == -1) 
                    return false;

                inv.ConsumeOne(arrowSlot);
                world.AddEntity(new ArrowEntity(player));
                Game.Instance.AudioManager.PlayAudio("Resources/Audio/Bow/BowRelease.ogg", Game.Instance.AudioManager.SfxVol);
                
                return true;
            }));
        Register(ItemType.Arrow, new ItemDef("Arrow", UvHelper.FromTileCoords(1, 2), MaxStackSize: 64));

        // Armor
        RegisterArmor(ItemType.LeatherHelmet, "Leather Helmet", (0, 11), ArmorSlot.Head, ArmorTier.Leather, 56, 1);
        RegisterArmor(ItemType.LeatherChest, "Leather Chestplate", (0, 10), ArmorSlot.Chest, ArmorTier.Leather, 81, 3);
        RegisterArmor(ItemType.LeatherLegs, "Leather Leggings", (0, 9), ArmorSlot.Legs, ArmorTier.Leather, 76, 2);
        RegisterArmor(ItemType.LeatherBoots, "Leather Boots", (0, 8), ArmorSlot.Feet, ArmorTier.Leather, 66, 1);

        RegisterArmor(ItemType.IronHelmet, "Iron Helmet", (2, 11), ArmorSlot.Head, ArmorTier.Iron, 166, 2);
        RegisterArmor(ItemType.IronChest, "Iron Chestplate", (2, 10), ArmorSlot.Chest, ArmorTier.Iron, 241, 6);
        RegisterArmor(ItemType.IronLegs, "Iron Leggings", (2, 9), ArmorSlot.Legs, ArmorTier.Iron, 226, 5);
        RegisterArmor(ItemType.IronBoots, "Iron Boots", (2, 8), ArmorSlot.Feet, ArmorTier.Iron, 196, 2);

        RegisterArmor(ItemType.GoldHelmet, "Gold Helmet", (3, 11), ArmorSlot.Head, ArmorTier.Gold, 78, 2);
        RegisterArmor(ItemType.GoldChest, "Gold Chestplate", (3, 10), ArmorSlot.Chest, ArmorTier.Gold, 113, 5);
        RegisterArmor(ItemType.GoldLegs, "Gold Leggings", (3, 9), ArmorSlot.Legs, ArmorTier.Gold, 106, 3);
        RegisterArmor(ItemType.GoldBoots, "Gold Boots", (3, 8), ArmorSlot.Feet, ArmorTier.Gold, 92, 1);

        RegisterArmor(ItemType.DiamondHelmet, "Diamond Helmet", (1, 11), ArmorSlot.Head, ArmorTier.Diamond, 364, 3);
        RegisterArmor(ItemType.DiamondChest, "Diamond Chestplate", (1, 10), ArmorSlot.Chest, ArmorTier.Diamond, 529, 8);
        RegisterArmor(ItemType.DiamondLegs, "Diamond Leggings", (1, 9), ArmorSlot.Legs, ArmorTier.Diamond, 496, 6);
        RegisterArmor(ItemType.DiamondBoots, "Diamond Boots", (1, 8), ArmorSlot.Feet, ArmorTier.Diamond, 430, 3);
    }

    private static void Register(ItemType type, string name, (int x, int y) tile, ToolType toolType, ToolTier tier, int durability, float miningSpeed, int attackDamage)
    {
        Items[type] = new ItemDef(name, UvHelper.FromTileCoords(tile.x, tile.y),
            toolType, tier, durability, miningSpeed, attackDamage);
    }

    private static void RegisterArmor(ItemType type, string name, (int x, int y) tile, ArmorSlot slot, ArmorTier tier, int durability, int armorPoints)
    {
        Items[type] = new ItemDef(name, UvHelper.FromTileCoords(tile.x, tile.y),
            MaxDurability: durability, ArmorSlot: slot, ArmorTier: tier, ArmorPoints: armorPoints);
    }

    public static void Register(ItemType type, ItemDef def) => Items[type] = def;

    public static ItemDef Get(ItemType type)
    {
        if (Items.TryGetValue(type, out var def))
            return def;

        throw new ArgumentException($"Unknown item type: {type}");
    }

    public static IEnumerable<(ItemType, ItemDef)> GetAll() => Items.Select(kv => (kv.Key, kv.Value));

    public static string GetName(ItemType type) => Get(type).Name;
    public static TextureCoords GetItemCoords(ItemType type) => Get(type).ItemCoords;
}
// Main class that manages block entities in the game world. Handles creation, retrieval, and destruction of block entities. | DA | 3/5/26
using System.Xml.Serialization;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.BlockEntities;

public static class BlockEntityManager
{
    private static readonly Dictionary<Vector3i, IBlockEntity> BlockEntities = new();

    public static ChestData GetOrCreateChest(Vector3i pos)
    {
        if (BlockEntities.TryGetValue(pos, out var e) && e is ChestData c)
            return c;

        var chest = new ChestData(pos);
        BlockEntities[pos] = chest;
        return chest;
    }

    public static FurnaceData GetOrCreateFurnace(Vector3i pos)
    {
        if (BlockEntities.TryGetValue(pos, out var e) && e is FurnaceData f)
            return f;

        var furnace = new FurnaceData(pos);
        BlockEntities[pos] = furnace;
        return furnace;
    }

    public static void DestroyAt(Vector3i pos, World world)
    {
        if (BlockEntities.TryGetValue(pos, out var furnace))
        {
            furnace.DropContents(world);
            BlockEntities.Remove(pos);
        }
    }

    public static void TickFurnaces()
    {
        var world = Game.Instance.GetWorld;

        foreach (var furnace in BlockEntities.Values.OfType<FurnaceData>())
            TickFurnace(furnace, world);
    }

    private static void TickFurnace(FurnaceData f, World world)
    {
        var recipe = SmeltRegistry.FindMatch(f.InputSlot);
        bool wasLit = f.IsLit;

        if (f.BurnTimeRemaining <= 0 && recipe != null && f.FuelSlot.HasValue)
        {
            int fuelTicks = SmeltRegistry.GetFuelValue(f.FuelSlot);
            if (fuelTicks > 0)
            {
                f.BurnTimeRemaining = fuelTicks;
                f.CurrentFuelMax = fuelTicks;
                ConsumeFuelSlot(f);
            }
        }

        if (f.BurnTimeRemaining <= 0)
        {
            f.SmeltProgress = 0;
            if (wasLit)
                SwapBlock(world, f.Position, BlockType.FurnaceLit, BlockType.Furnace);

            return;
        }

        f.BurnTimeRemaining--;

        if (!wasLit)
        {
            SwapBlock(world, f.Position, BlockType.Furnace, BlockType.FurnaceLit);
        }
        else if (f.BurnTimeRemaining <= 0 && SmeltRegistry.GetFuelValue(f.FuelSlot) <= 0)
        {
            SwapBlock(world, f.Position, BlockType.FurnaceLit, BlockType.Furnace);
        }

        if (recipe == null || !CanOutput(f, recipe.Output))
        {
            f.SmeltProgress = 0;
            return;
        }

        if (++f.SmeltProgress >= recipe.TicksToSmelt)
        {
            ProduceOutput(f, recipe);
            f.SmeltProgress = 0;
        }
    }

    private static void SwapBlock(World world, Vector3i pos, BlockType from, BlockType to)
    {
        if (world.GetBlock(pos.X, pos.Y, pos.Z) != from)
            return;

        int meta = world.GetMetadata(pos.X, pos.Y, pos.Z);

        world.SetBlock(pos.X, pos.Y, pos.Z, to);

        if (meta != 0)
            world.SetMetadata(pos.X, pos.Y, pos.Z, (byte)meta);

        world.SetChunkAsModified(pos.X, pos.Y, pos.Z);
    }

    private static bool CanOutput(FurnaceData f, ItemStack result)
    {
        if (!f.OutputSlot.HasValue)
            return true;

        if (f.OutputSlot.Value != result)
            return false;

        int max = result.IsBlock
            ? BlockRegistry.Get(result.Block).MaxStackSize
            : ItemRegistry.Get(result.Item).MaxStackSize;

        return f.OutputSlot.Value.Count < max;
    }

    private static void ProduceOutput(FurnaceData f, SmeltRecipe recipe)
    {
        var input = f.InputSlot!.Value;
        f.InputSlot = input.Count <= 1 ? null : input.WithCount(input.Count - 1);

        f.OutputSlot = f.OutputSlot.HasValue
            ? f.OutputSlot.Value.WithCount(f.OutputSlot.Value.Count + recipe.Output.Count)
            : recipe.Output;
    }

    private static void ConsumeFuelSlot(FurnaceData f)
    {
        var fuel = f.FuelSlot!.Value;
        f.FuelSlot = fuel.Count <= 1 ? null : fuel.WithCount(fuel.Count - 1);
    }

    public static void Clear() => BlockEntities.Clear();

    public static void Save(string worldSavePath)
    {
        var file = new BlockEntityFile();

        foreach (var entity in BlockEntities.Values)
        {
            if (entity is FurnaceData f)
            {
                file.Furnaces.Add(new SerializableFurnace
                {
                    X                 = f.Position.X,
                    Y                 = f.Position.Y,
                    Z                 = f.Position.Z,
                    Input             = f.InputSlot.HasValue  ? SerializableStack.From(f.InputSlot.Value)  : null,
                    Fuel              = f.FuelSlot.HasValue   ? SerializableStack.From(f.FuelSlot.Value)   : null,
                    Output            = f.OutputSlot.HasValue ? SerializableStack.From(f.OutputSlot.Value) : null,
                    BurnTimeRemaining = f.BurnTimeRemaining,
                    SmeltProgress     = f.SmeltProgress,
                });
            }
            else if (entity is ChestData c)
            {
                var sc = new SerializableChest { X = c.Position.X, Y = c.Position.Y, Z = c.Position.Z };
                for (int i = 0; i < ChestData.CHEST_SLOTS; i++)
                {
                    var slot = c.GetSlot(i);
                    if (slot.HasValue)
                        sc.Slots.Add(new SerializableChestSlot { Index = i, Stack = SerializableStack.From(slot.Value) });
                }
                file.Chests.Add(sc);
            }
        }

        string path = Path.Combine(worldSavePath, "block_entities.xml");
        var serializer = new XmlSerializer(typeof(BlockEntityFile));
        using var stream = new FileStream(path, FileMode.Create);
        serializer.Serialize(stream, file);
    }

    public static void Load(string worldSavePath)
    {
        Clear();
        string path = Path.Combine(worldSavePath, "block_entities.xml");

        if (!File.Exists(path))
            return;

        var serializer = new XmlSerializer(typeof(BlockEntityFile));
        BlockEntityFile file;
        using (var stream = new FileStream(path, FileMode.Open))
            file = (BlockEntityFile)serializer.Deserialize(stream)!;

        foreach (var entry in file.Furnaces)
        {
            var pos = new Vector3i(entry.X, entry.Y, entry.Z);
            BlockEntities[pos] = new FurnaceData(pos)
            {
                InputSlot         = entry.Input?.ToItemStack(),
                FuelSlot          = entry.Fuel?.ToItemStack(),
                OutputSlot        = entry.Output?.ToItemStack(),
                BurnTimeRemaining = entry.BurnTimeRemaining,
                SmeltProgress     = entry.SmeltProgress,
            };
        }

        foreach (var entry in file.Chests)
        {
            var pos   = new Vector3i(entry.X, entry.Y, entry.Z);
            var chest = new ChestData(pos);
            foreach (var s in entry.Slots)
                chest.SetSlot(s.Index, s.Stack.ToItemStack());
            BlockEntities[pos] = chest;
        }
    }
}

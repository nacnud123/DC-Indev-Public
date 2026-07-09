// Main class that manages block entities in the game world. Handles creation, retrieval, and destruction of block entities. | DA | 3/5/26

using System.Xml.Serialization;

using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.BlockEntities;

/// <summary>
/// Static registry of all "block entities" in the currently loaded world - chests, double chests, and furnaces - keyed by their world-space block position. Block entities hold state that a plain block byte in the chunk data cannot (inventory slots, furnace burn progress, etc). This class is the single source of truth for that state: chunk meshing/serialization only knows about the block type/metadata, while this manager tracks the "rich" data attached to specific positions. Also owns furnace ticking (fuel consumption / smelting progress) and XML save/load of all block entity data to a separate file from the chunk binaries.
/// </summary>
public static class BlockEntityManager
{
    // Keyed by world position (not chunk-local) so lookups from raycasts/interactions are O(1) regardless of which chunk the position falls in.
    private static readonly Dictionary<Vector3i, IBlockEntity> BlockEntities = new();

    /// <summary>Returns the chest at <paramref name="pos"/>, creating and registering a new empty one if none exists yet.</summary>
    public static ChestData GetOrCreateChest(Vector3i pos)
    {
        if (BlockEntities.TryGetValue(pos, out var e) && e is ChestData c)
            return c;

        var chest = new ChestData(pos);
        BlockEntities[pos] = chest;
        return chest;
    }

    /// <summary>Returns the chest at <paramref name="pos"/> only if one is already registered; does not create one.</summary>
    public static ChestData? GetChestIfExists(Vector3i pos) =>
        BlockEntities.TryGetValue(pos, out var e) && e is ChestData c ? c : null;

    /// <summary>Directly registers (or overwrites) a chest at a position, e.g. when pairing up a double chest's other half.</summary>
    public static void RegisterChest(Vector3i pos, ChestData chest) => BlockEntities[pos] = chest;

    /// <summary>Returns the double chest at <paramref name="pos"/>, creating and registering a new empty one if none exists yet.</summary>
    public static DoubleChestData GetOrCreateDoubleChest(Vector3i pos)
    {
        if (BlockEntities.TryGetValue(pos, out var e) && e is DoubleChestData d)
            return d;

        var dc = new DoubleChestData(pos);
        BlockEntities[pos] = dc;
        return dc;
    }

    /// <summary>Returns the furnace at <paramref name="pos"/>, creating and registering a new empty one if none exists yet.</summary>
    public static FurnaceData GetOrCreateFurnace(Vector3i pos)
    {
        if (BlockEntities.TryGetValue(pos, out var e) && e is FurnaceData f)
            return f;

        var furnace = new FurnaceData(pos);
        BlockEntities[pos] = furnace;
        return furnace;
    }

    /// <summary>
    /// Removes the block entity at <paramref name="pos"/> (e.g. because the block was broken), dropping any items it held into the world first.
    /// </summary>
    public static void DestroyAt(Vector3i pos, World world)
    {
        if (BlockEntities.TryGetValue(pos, out var entity))
        {
            entity.DropContents(world);
            BlockEntities.Remove(pos);
        }
    }

    /// <summary>Removes the block entity at a position without dropping its contents (used when contents are handled elsewhere).</summary>
    public static void Remove(Vector3i pos) => BlockEntities.Remove(pos);

    /// <summary>Advances one game tick for every furnace currently registered. Called once per world tick.</summary>
    public static void TickFurnaces()
    {
        var world = Game.Instance.GetWorld;

        foreach (var furnace in BlockEntities.Values.OfType<FurnaceData>())
            TickFurnace(furnace, world);
    }

    /// <summary>
    /// Advances a single furnace by one tick: consumes fuel when needed, swaps the world block between lit/unlit furnace variants to match burn state, and progresses/produces smelting output.
    /// </summary>
    private static void TickFurnace(FurnaceData f, World world)
    {
        var recipe = SmeltRegistry.FindMatch(f.InputSlot);
        bool wasLit = f.IsLit;

        // Out of fuel but there's a valid recipe and fuel available - consume one fuel item to relight.
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
            // No fuel and none available - furnace is unlit, reset smelt progress and swap block back if needed.
            f.SmeltProgress = 0;
            if (wasLit)
                SwapBlock(world, f.Position, BlockType.FurnaceLit, BlockType.Furnace);

            return;
        }

        f.BurnTimeRemaining--;

        if (!wasLit)
        {
            // Just became lit this tick - swap the world block to the lit variant (changes light emission/texture).
            SwapBlock(world, f.Position, BlockType.Furnace, BlockType.FurnaceLit);
        }
        else if (f.BurnTimeRemaining <= 0 && SmeltRegistry.GetFuelValue(f.FuelSlot) <= 0)
        {
            // Burned through the last fuel unit with nothing queued up - go dark.
            SwapBlock(world, f.Position, BlockType.FurnaceLit, BlockType.Furnace);
        }

        if (recipe == null || !CanOutput(f, recipe.Output))
        {
            // No valid recipe, or output slot is full/mismatched - can't make progress.
            f.SmeltProgress = 0;
            return;
        }

        if (++f.SmeltProgress >= recipe.TicksToSmelt)
        {
            ProduceOutput(f, recipe);
            f.SmeltProgress = 0;
        }
    }

    /// <summary>
    /// Replaces the world block at <paramref name="pos"/> with <paramref name="to"/> only if it currently matches <paramref name="from"/>, preserving metadata (facing, etc.) across the swap and flagging the owning chunk dirty so it gets remeshed/resaved.
    /// </summary>
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

    /// <summary>Checks whether a smelt result can be placed into the furnace's output slot (empty, or same item type with room left in the stack).</summary>
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

    /// <summary>Consumes one input item and adds (or starts) the recipe's output stack once smelting finishes.</summary>
    private static void ProduceOutput(FurnaceData f, SmeltRecipe recipe)
    {
        var input = f.InputSlot!.Value;
        f.InputSlot = input.Count <= 1 ? null : input.WithCount(input.Count - 1);

        f.OutputSlot = f.OutputSlot.HasValue
            ? f.OutputSlot.Value.WithCount(f.OutputSlot.Value.Count + recipe.Output.Count)
            : recipe.Output;
    }

    /// <summary>Consumes one unit of fuel from the fuel slot, clearing the slot if it was the last one.</summary>
    private static void ConsumeFuelSlot(FurnaceData f)
    {
        var fuel = f.FuelSlot!.Value;
        f.FuelSlot = fuel.Count <= 1 ? null : fuel.WithCount(fuel.Count - 1);
    }

    /// <summary>Removes all registered block entities, e.g. before loading a new/different world.</summary>
    public static void Clear() => BlockEntities.Clear();

    /// <summary>
    /// Serializes every registered furnace/chest/double chest to <c>block_entities.xml</c> under the given world save directory. Stored separately from chunk binaries because block entity data (inventory contents, burn state) doesn't fit the compact per-block chunk format.
    /// </summary>
    public static void Save(string worldSavePath)
    {
        var file = new BlockEntityFile();

        foreach (var entity in BlockEntities.Values)
        {
            if (entity is FurnaceData f)
            {
                file.Furnaces.Add(new SerializableFurnace
                {
                    X = f.Position.X,
                    Y = f.Position.Y,
                    Z = f.Position.Z,
                    Input = f.InputSlot.HasValue ? SerializableStack.From(f.InputSlot.Value) : null,
                    Fuel = f.FuelSlot.HasValue ? SerializableStack.From(f.FuelSlot.Value) : null,
                    Output = f.OutputSlot.HasValue ? SerializableStack.From(f.OutputSlot.Value) : null,
                    BurnTimeRemaining = f.BurnTimeRemaining,
                    SmeltProgress = f.SmeltProgress,
                });
            }
            else if (entity is ChestData c)
            {
                var sc = new SerializableChest { X = c.Position.X, Y = c.Position.Y, Z = c.Position.Z };
                for (int i = 0; i < ChestData.CHEST_SLOTS; i++)
                {
                    var slot = c.GetSlot(i);
                    if (slot.HasValue)
                        sc.Slots.Add(
                            new SerializableChestSlot { Index = i, Stack = SerializableStack.From(slot.Value) });
                }

                file.Chests.Add(sc);
            }
            else if (entity is DoubleChestData dc)
            {
                var sdc = new SerializableDoubleChest { X = dc.Position.X, Y = dc.Position.Y, Z = dc.Position.Z };
                for (int i = 0; i < DoubleChestData.CHEST_SLOTS; i++)
                {
                    var slot = dc.GetSlot(i);
                    if (slot.HasValue)
                        sdc.Slots.Add(new SerializableChestSlot
                            { Index = i, Stack = SerializableStack.From(slot.Value) });
                }

                file.DoubleChests.Add(sdc);
            }
        }

        string path = Path.Combine(worldSavePath, "block_entities.xml");
        var serializer = new XmlSerializer(typeof(BlockEntityFile));
        using var stream = new FileStream(path, FileMode.Create);
        serializer.Serialize(stream, file);
    }

    /// <summary>
    /// Clears the current registry and repopulates it from <c>block_entities.xml</c> in the given world save directory. No-op (leaves the registry empty) if the file doesn't exist yet, e.g. a save created before block entities existed, or a world with no chests/furnaces.
    /// </summary>
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
                InputSlot = entry.Input?.ToItemStack(),
                FuelSlot = entry.Fuel?.ToItemStack(),
                OutputSlot = entry.Output?.ToItemStack(),
                BurnTimeRemaining = entry.BurnTimeRemaining,
                SmeltProgress = entry.SmeltProgress,
            };
        }

        foreach (var entry in file.Chests)
        {
            var pos = new Vector3i(entry.X, entry.Y, entry.Z);
            var chest = new ChestData(pos);
            foreach (var s in entry.Slots)
                chest.SetSlot(s.Index, s.Stack.ToItemStack());
            BlockEntities[pos] = chest;
        }

        foreach (var entry in file.DoubleChests)
        {
            var pos = new Vector3i(entry.X, entry.Y, entry.Z);
            var dc = new DoubleChestData(pos);
            foreach (var s in entry.Slots)
                dc.SetSlot(s.Index, s.Stack.ToItemStack());
            BlockEntities[pos] = dc;
        }
    }
}
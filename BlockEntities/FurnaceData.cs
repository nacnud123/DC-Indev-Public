// The main class used for the Furnace block entity. Just holds reference to the furnace's inventory and burn state. Also, has function to drop it's contents. | DA | 3/5/26


using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.BlockEntities;

/// <summary>
/// Block entity backing a furnace block: three inventory slots (input/fuel/output) plus burn and smelt-progress counters. All tick logic (fuel consumption, smelting, lit/unlit block swapping) lives in <see cref="BlockEntityManager.TickFurnaces"/> - this class is purely the data container.
/// </summary>
public class FurnaceData : IBlockEntity
{
    public Vector3i Position { get; set; }

    public ItemStack? InputSlot;   // item being smelted
    public ItemStack? FuelSlot;    // item being burned as fuel
    public ItemStack? OutputSlot;  // smelted result awaiting collection

    public int BurnTimeRemaining;  // ticks left before the current fuel unit is exhausted
    public int SmeltProgress;      // ticks accumulated toward completing the current smelt recipe
    public int CurrentFuelMax; // burn time of the fuel unit currently loaded, for progress bar

    /// <summary>True while there is unburned fuel remaining; drives the lit-furnace block/texture swap.</summary>
    public bool IsLit => BurnTimeRemaining > 0;

    public FurnaceData(Vector3i pos)
    {
        Position = pos;
    }

    /// <summary>Drops the input, fuel, and output stacks (if any) as item entities at the furnace position, then clears all slots.</summary>
    public void DropContents(World world)
    {
        var center = new Vector3(Position.X + 0.5f, Position.Y + 0.5f, Position.Z + 0.5f);

        if (InputSlot.HasValue)
            world.AddEntity(new DroppedItemEntity(center, InputSlot.Value, Game.Instance.WorldTexture));

        if (FuelSlot.HasValue)
            world.AddEntity(new DroppedItemEntity(center, FuelSlot.Value, Game.Instance.WorldTexture));

        if (OutputSlot.HasValue)
            world.AddEntity(new DroppedItemEntity(center, OutputSlot.Value, Game.Instance.WorldTexture));

        InputSlot = FuelSlot = OutputSlot = null;
    }
}
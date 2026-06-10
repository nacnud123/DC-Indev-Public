// The main class used for the Furnace block entity. Just holds reference to the furnace's inventory and burn state. Also, has function to drop it's contents. | DA | 3/5/26

using OpenTK.Mathematics;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.BlockEntities;

public class FurnaceData : IBlockEntity
{
    public Vector3i Position { get; set; }

    public ItemStack? InputSlot;
    public ItemStack? FuelSlot;
    public ItemStack? OutputSlot;

    public int BurnTimeRemaining;
    public int SmeltProgress;
    public int CurrentFuelMax; // burn time of the fuel unit currently loaded, for progress bar

    public bool IsLit => BurnTimeRemaining > 0;

    public FurnaceData(Vector3i pos)
    {
        Position = pos;
    }

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
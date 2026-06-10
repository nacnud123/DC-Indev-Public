// Main class for the chest block entity. Holds reference to the chest's inventory. | DA | 3/5/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.BlockEntities;

public class ChestData : IBlockEntity
{
    public const int CHEST_SLOTS = 27;

    public Vector3i Position { get; set; }
    private readonly ItemStack?[] mSlots = new ItemStack?[CHEST_SLOTS];

    public ChestData(Vector3i position)
    {
        Position = position;
    }

    public ItemStack? GetSlot(int index) => mSlots[index];

    public void SetSlot(int index, ItemStack? stack) => mSlots[index] = stack;

    public bool TryAdd(ItemStack stack)
    {
        int remaining = stack.Count;

        for (int i = 0; i < CHEST_SLOTS && remaining > 0; i++)
        {
            if (mSlots[i] == null)
                continue;

            var slot = mSlots[i]!.Value;

            if (slot != stack)
                continue;

            int max = GetMaxStackSize(stack);

            if (slot.Count >= max)
                continue;

            int transfer = Math.Min(max - slot.Count, remaining);
            mSlots[i] = slot.WithCount(slot.Count + transfer);
            remaining -= transfer;
        }

        for (int i = 0; i < CHEST_SLOTS && remaining > 0; i++)
        {
            if (mSlots[i] != null)
                continue;

            int place = Math.Min(GetMaxStackSize(stack), remaining);
            mSlots[i] = stack.WithCount(place);
            remaining -= place;
        }

        return remaining < stack.Count;
    }

    public void DropContents(World world)
    {
        var center = new Vector3(Position.X + 0.5f, Position.Y + 0.5f, Position.Z + 0.5f);
        foreach (var item in mSlots)
        {
            if (item.HasValue)
                world.AddEntity(new DroppedItemEntity(center, item.Value, Game.Instance.WorldTexture));
        }
    }

    private int GetMaxStackSize(ItemStack stack)
    {
        if (stack.IsBlock)
            return BlockRegistry.Get(stack.Block).MaxStackSize;
        
        return ItemRegistry.Get(stack.Item).MaxStackSize;
    }
}
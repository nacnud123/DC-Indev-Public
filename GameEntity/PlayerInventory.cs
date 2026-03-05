using VoxelEngine.Items;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public class PlayerInventory
{
    public const int MAIN_SLOTS = 27;
    public const int HOTBAR_SLOTS = 9;
    public const int ARMOR_SLOTS = 4;
    public const int HOTBAR_START = MAIN_SLOTS; // 27
    public const int ARMOR_START = MAIN_SLOTS + HOTBAR_SLOTS; // 36
    public const int TOTAL_SLOTS = MAIN_SLOTS + HOTBAR_SLOTS + ARMOR_SLOTS; // 40

    private ItemStack?[] mSlots = new ItemStack?[TOTAL_SLOTS];
    public Span<ItemStack?> HotbarSlots => mSlots[HOTBAR_START..ARMOR_START];

    // Try to add a stack, checking the hotbar first then the main inventory.
    public bool TryAdd(ItemStack stack)
    {
        int remaining = stack.Count;
        int maxStack = GetMaxStackSize(stack);

        // Merge pass: hotbar first, then main inventory
        remaining = MergeInto(stack, remaining, maxStack, HOTBAR_START, ARMOR_START);
        remaining = MergeInto(stack, remaining, maxStack, 0, HOTBAR_START);

        // Fill pass: hotbar first, then main inventory
        remaining = FillInto(stack, remaining, maxStack, HOTBAR_START, ARMOR_START);
        remaining = FillInto(stack, remaining, maxStack, 0, HOTBAR_START);

        return remaining < stack.Count;
    }

    private int MergeInto(ItemStack stack, int remaining, int maxStack, int from, int to)
    {
        for (int i = from; i < to && remaining > 0; i++)
        {
            if (mSlots[i] == null)
                continue;

            var slot = mSlots[i]!.Value;
            if (slot != stack)
                continue;

            if (slot.Count >= maxStack)
                continue;

            int transfer = Math.Min(maxStack - slot.Count, remaining);
            mSlots[i] = slot.WithCount(slot.Count + transfer);
            remaining -= transfer;
        }

        return remaining;
    }

    private int FillInto(ItemStack stack, int remaining, int maxStack, int from, int to)
    {
        for (int i = from; i < to && remaining > 0; i++)
        {
            if (mSlots[i] != null)
                continue;

            int place = Math.Min(maxStack, remaining);
            mSlots[i] = stack.WithCount(place);
            remaining -= place;
        }

        return remaining;
    }

    public ItemStack? GetSlot(int index) =>
        index >= 0 && index < TOTAL_SLOTS ? mSlots[index] : null;

    public void SetSlot(int index, ItemStack? stack)
    {
        if (index >= 0 && index < TOTAL_SLOTS)
            mSlots[index] = stack;
    }

    public bool TryGetSlot(int index, out ItemStack stack)
    {
        if (index >= 0 && index < TOTAL_SLOTS && mSlots[index].HasValue)
        {
            stack = mSlots[index]!.Value;
            return true;
        }

        stack = default;
        return false;
    }

    public void DamageTool(int slotIndex, int amount = 1)
    {
        if (slotIndex < 0 || slotIndex >= TOTAL_SLOTS)
            return;

        if (!mSlots[slotIndex].HasValue)
            return;

        var slot = mSlots[slotIndex]!.Value;
        if (!slot.HasDurability)
            return;

        int newDur = slot.Durability - amount;
        mSlots[slotIndex] = newDur <= 0 ? null : slot.WithDurability(newDur);
    }

    public void ConsumeOne(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= TOTAL_SLOTS)
            return;

        if (!mSlots[slotIndex].HasValue)
            return;


        var slot = mSlots[slotIndex]!.Value;
        mSlots[slotIndex] = slot.Count <= 1 ? null : slot.WithCount(slot.Count - 1);
    }

    public int FindItem(ItemType type)
    {
        for (int i = 0; i < ARMOR_START; i++)
        {
            var slot = mSlots[i];
            if (slot.HasValue && !slot.Value.IsBlock && slot.Value.Item == type)
                return i;
        }

        return -1;
    }

    public ItemStack? GetArmorSlot(ArmorSlot slot) => mSlots[ARMOR_START + (int)slot];
    public void SetArmorSlot(ArmorSlot slot, ItemStack? stack) => mSlots[ARMOR_START + (int)slot] = stack;

    public int GetArmorValue()
    {
        int totalDamageReduce = 0;
        int totalDurabilityRemaining = 0;
        int totalMaxDurability = 0;

        for (int i = 0; i < ARMOR_SLOTS; i++)
        {
            var slot = mSlots[ARMOR_START + i];
            if (!slot.HasValue || slot.Value.IsBlock)
                continue;

            var def = ItemRegistry.Get(slot.Value.Item);
            if (def.MaxDurability <= 0)
                continue;

            totalDamageReduce += def.ArmorPoints;
            totalDurabilityRemaining += slot.Value.Durability;
            totalMaxDurability += def.MaxDurability;
        }

        if (totalDamageReduce == 0 || totalMaxDurability == 0)
            return 0;

        // armorValue = (totalDamageReduceAmount - 1) * totalDurabilityRemaining / totalMaxDurability + 1
        return (totalDamageReduce - 1) * totalDurabilityRemaining / totalMaxDurability + 1;
    }

    // Damage every equipped armor piece by rawDamage
    public void DamageArmor(int rawDamage)
    {
        for (int i = 0; i < ARMOR_SLOTS; i++)
        {
            DamageTool(ARMOR_START + i, rawDamage);
        }
    }

    public List<SavedSlot> SaveToSlots()
    {
        var list = new List<SavedSlot>();
        for (int i = 0; i < TOTAL_SLOTS; i++)
        {
            var slot = mSlots[i];
            if (!slot.HasValue)
                continue;

            list.Add(new SavedSlot
            {
                Index = i,
                IsBlock = slot.Value.IsBlock,
                Type = slot.Value.IsBlock ? slot.Value.Block.ToString() : slot.Value.Item.ToString(),
                Count = slot.Value.Count,
                Durability = slot.Value.Durability,
            });
        }

        return list;
    }

    public void LoadFromSlots(List<SavedSlot> slots)
    {
        Array.Clear(mSlots, 0, mSlots.Length);
        foreach (var s in slots)
        {
            if (s.Index < 0 || s.Index >= TOTAL_SLOTS)
                continue;

            if (s.IsBlock)
            {
                if (Enum.TryParse<BlockType>(s.Type, out var block))
                    mSlots[s.Index] = ItemStack.FromBlock(block, s.Count);
            }
            else
            {
                if (Enum.TryParse<ItemType>(s.Type, out var item))
                {
                    var stack = ItemStack.FromItem(item, s.Count);
                    mSlots[s.Index] = s.Durability >= 0 ? stack.WithDurability(s.Durability) : stack;
                }
            }
        }
    }

    private static int GetMaxStackSize(ItemStack stack)
    {
        if (stack.IsBlock)
            return BlockRegistry.Get(stack.Block).MaxStackSize;

        return ItemRegistry.Get(stack.Item).MaxStackSize;
    }
}
using VoxelEngine.Items;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// The player's entire item storage: a single flat array of TOTAL_SLOTS ItemStack? slots, laid out as [0, MAIN_SLOTS) main inventory, [HOTBAR_START, ARMOR_START) hotbar, and [ARMOR_START, TOTAL_SLOTS) armor (indexed by ArmorSlot). Any code indexing mSlots directly must respect this layout — off-by-one errors here silently corrupt the wrong region (e.g. writing into armor slots when main-inventory logic overruns). Serialization (SaveToSlots/LoadFromSlots) preserves absolute slot indices so saves stay correct even if slot contents shift around.
/// </summary>
public class PlayerInventory
{
    public const int MAIN_SLOTS = 27;
    public const int HOTBAR_SLOTS = 9;
    public const int ARMOR_SLOTS = 4;
    // Slot layout (indices, half-open ranges): [0, HOTBAR_START)          = main inventory (27 slots) [HOTBAR_START, ARMOR_START) = hotbar (9 slots) — HOTBAR_START is the index of hotbar slot 0 [ARMOR_START, TOTAL_SLOTS)  = armor (4 slots, indexed by the ArmorSlot enum)
    public const int HOTBAR_START = MAIN_SLOTS; // 27
    public const int ARMOR_START = MAIN_SLOTS + HOTBAR_SLOTS; // 36
    public const int TOTAL_SLOTS = MAIN_SLOTS + HOTBAR_SLOTS + ARMOR_SLOTS; // 40

    private ItemStack?[] mSlots = new ItemStack?[TOTAL_SLOTS];
    public Span<ItemStack?> HotbarSlots => mSlots[HOTBAR_START..ARMOR_START];

    /// <summary>
    /// Attempts to add an item stack to the inventory, preferring the hotbar over the main inventory in both passes. Two-pass strategy: first merge into existing matching/partial stacks (to avoid fragmenting items across more slots than necessary), then fill any remaining count into empty slots. Returns true if at least part of the stack was placed (a partial add still returns true — check the caller's remaining-count logic if full-or-nothing is needed).
    /// </summary>
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

    /// <summary>Adds `remaining` units of `stack` into existing slots in [from, to) that already hold a matching, non-full stack. Returns leftover units that didn't fit.</summary>
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

    /// <summary>Places `remaining` units of `stack` into empty slots in [from, to), creating new stacks up to maxStack each. Returns leftover units that didn't fit (inventory full).</summary>
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

    /// <summary>
    /// Reduces the durability of the item in slotIndex by `amount`; the stack is destroyed (slot set to null) when durability drops to zero or below. No-op for items without durability (blocks, non-tool items) since HasDurability is false for those.
    /// </summary>
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

    /// <summary>Removes a single unit from the stack in slotIndex, clearing the slot entirely if that was the last one.</summary>
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

    private int GetMaxStackSize(ItemStack stack)
    {
        if (stack.IsBlock)
            return BlockRegistry.Get(stack.Block).MaxStackSize;

        return ItemRegistry.Get(stack.Item).MaxStackSize;
    }
}
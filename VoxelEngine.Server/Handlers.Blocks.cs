// Dig and place. The server decides; clients are told. | Stage 8

using VoxelEngine.BlockEntities;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    /// Generous: the client's own raycast reaches ~5 blocks, and this only has to stop someone
    /// editing the world from across the map.
    private const float BLOCK_REACH = 7f;

    /// A dig is accepted at half the time it should have taken. Tight enough that instant-break
    /// claims on stone are rejected, loose enough that a laggy frame isn't punished.
    private const float DIG_TIME_TOLERANCE = 0.5f;

    private void HandleDigging(ServerPlayer player, NetStream r)
    {
        byte status = r.ReadByte();               // 0 start, 2 finished
        int x = r.ReadInt();
        byte y = r.ReadByte();
        int z = r.ReadInt();
        r.ReadByte();                             // face

        // Beta reused this packet for "drop what I'm holding", which is status 4 with no position.
        if (status == 4)
        {
            DropHeldItem(player);
            return;
        }

        var pos = new Vector3i(x, y, z);

        // Status 0 is the client saying it has started; it's what makes the timing check below
        // possible at all.
        if (status == 0)
        {
            player.DiggingAt = pos;
            player.DiggingSinceTick = mTickCount;
            return;
        }

        if (status != 2)                          // beta only acted on "finished"
            return;

        var block = mWorld.GetBlock(pos);

        if (block == BlockType.Air)
            return;

        if (block == BlockType.Bedrock && !player.IsOp)
            return;

        if (!InReach(player, pos) || !DigTookLongEnough(player, pos, block))
        {
            // Tell them what is actually there, so a rejected break doesn't leave a hole on their
            // screen that nobody else can see.
            SendBlockTo(player, pos);
            return;
        }

        player.DiggingAt = null;

        // Same rule the client mines by: without the tier check you can pull diamond out of stone
        // with your fist on a server, and tools would never wear out.
        int heldIndex = PlayerInventory.HOTBAR_START +
                        Math.Clamp((int)player.HeldSlot, 0, PlayerInventory.HOTBAR_SLOTS - 1);

        var tool = player.Inventory.GetSlot(heldIndex) is { IsBlock: false } heldItem
                   && ItemRegistry.Get(heldItem.Item) is { IsTool: true } definition
            ? definition
            : null;

        var minTier = BlockRegistry.Get(block).MinimumTier;
        bool tierMet = minTier == ToolTier.None || (tool != null && tool.ToolTier >= minTier);

        var drop = tierMet ? BlockRegistry.GetDrop(block, (byte)mWorld.GetMetadata(pos)) : null;

        if (tool != null)
        {
            player.Inventory.DamageTool(heldIndex);
            SendInventory(player);
        }

        // The singleplayer path (Player.Interaction) does this; the server path never did. Without
        // it the block entity outlives the block: a broken chest keeps its items, so they reappear
        // in the next chest placed on that spot, and a broken furnace goes on smelting forever in
        // BlockEntityManager.TickFurnaces.
        CloseWindowsAt(pos);
        BlockEntityManager.DestroyAt(pos, mWorld);

        mWorld.SetBlock(pos, BlockType.Air);

        // Sand or gravel resting on what was just mined collapses. The server owns this - clients
        // skip their local copy - and the entities replicate through the normal object tracking.
        FallingBlockEntity.CollapseColumnAbove(mWorld, pos, RegisterFallingBlock);

        // Everyone in range hears it break and sees the particles; the digger's own client already
        // played both locally.
        BroadcastEffect(pos, EffectId.BlockBreak, (int)block);

        // Straight into the digger's inventory, so they don't have to walk over their own drop.
        // Only what won't fit becomes an entity.
        if (drop is { } dropped)
        {
            // Whatever didn't fit becomes an entity. Testing TryAdd's bool here instead lost the
            // remainder of a partial add outright: with one slot free, mining a stack of gravel put
            // some in your pocket and deleted the rest.
            if (player.Inventory.AddGetRemainder(dropped) is { } leftover)
                SpawnDrop(pos.ToVector3() + new Vector3(0.5f, 0.25f, 0.5f), leftover, player);

            SendInventory(player);
        }
    }

    private void HandlePlacement(ServerPlayer player, NetStream r)
    {
        int x = r.ReadInt();
        byte y = r.ReadByte();
        int z = r.ReadInt();
        byte direction = r.ReadByte();
        r.ReadItem();                             // what the client claims to hold; not trusted

        // Beta sends direction 255 with y 255 for "used an item in the air" - nothing to place
        // against, but a bow or a bucket still works.
        bool hasTarget = direction <= 5;
        var clicked = new Vector3i(x, y, z);

        // Beta reused this packet for "use block": right-clicking a container opens it instead of
        // placing. This is the only thing that ever calls OpenWindow.
        if (hasTarget && TryOpenContainer(player, clicked))
            return;

        // The held slot is the server's record, not the packet's claim - otherwise a client can
        // place any block by naming it.
        int heldIndex = PlayerInventory.HOTBAR_START +
                        Math.Clamp((int)player.HeldSlot, 0, PlayerInventory.HOTBAR_SLOTS - 1);

        if (player.Inventory.GetSlot(heldIndex) is not { } heldStack)
            return;

        if (!heldStack.IsBlock)
        {
            UseHeldItem(player, heldStack, heldIndex, clicked, hasTarget ? clicked + FaceOffset(direction) : null);
            return;
        }

        if (!hasTarget)
            return;

        var held = heldStack;
        var target = new Vector3i(x, y, z) + FaceOffset(direction);
        var existing = mWorld.GetBlock(target);

        if (existing != BlockType.Air && !BlockRegistry.Get(existing).IsReplaceable)
            return;

        if (!InReach(player, target))
        {
            SendBlockTo(player, target);
            return;
        }

        mWorld.SetBlock(target, held.Block);

        // Facing. Without this every stair, chest, furnace and torch placed on a server comes out
        // with metadata 0 - all facing the same way, torches always on the floor.
        byte metadata = PlacementMetadata(player, held.Block, clicked, target);
        if (metadata != 0)
            mWorld.SetMetadata(target, metadata);

        // Sand or gravel placed into mid-air starts falling straight away.
        if (BlockRegistry.IsGravityBlock(held.Block)
            && !BlockRegistry.IsSolid(mWorld.GetBlock(target + new Vector3i(0, -1, 0))))
        {
            FallingBlockEntity.SpawnFrom(mWorld, target, RegisterFallingBlock);
        }

        player.Inventory.ConsumeOne(heldIndex);
        SendInventory(player);
        BroadcastHeldItem(player);                // the stack may have run out
    }

    /// Falling blocks replicate as Beta "objects" (type 70/71), and object tracking only picks up
    /// entities that already carry a server-assigned network id - so give it one at spawn.
    private static void RegisterFallingBlock(FallingBlockEntity entity)
    {
        entity.AssignNetworkId(Entity.AllocateId());
        entity.LastSentPosition = entity.Position;
    }

    /// Blocks out of arm's reach aren't edited, however loudly the packet claims otherwise.
    private bool InReach(ServerPlayer player, Vector3i pos)
    {
        var centre = pos.ToVector3() + new Vector3(0.5f, 0.5f, 0.5f);
        var eyes = player.Position + new Vector3(0f, 1.62f, 0f);

        return (centre - eyes).Length() <= BLOCK_REACH;
    }

    /// The break has to have taken roughly as long as the block and the tool say it should. Without
    /// a matching "started digging" the only thing accepted is a block that breaks instantly.
    private bool DigTookLongEnough(ServerPlayer player, Vector3i pos, BlockType block)
    {
        int heldIndex = PlayerInventory.HOTBAR_START +
                        Math.Clamp((int)player.HeldSlot, 0, PlayerInventory.HOTBAR_SLOTS - 1);

        int required = RequiredDigTicks(block, player.Inventory.GetSlot(heldIndex));
        if (required == 0)
            return true;

        if (player.DiggingAt != pos)
            return false;

        return mTickCount - player.DiggingSinceTick >= required * DIG_TIME_TOLERANCE;
    }

    /// One block's true state, to one player - the correction after a rejected edit.
    private void SendBlockTo(ServerPlayer player, Vector3i pos) =>
        SendTo(player, PacketId.BlockChange, w =>
        {
            w.WriteInt(pos.X);
            w.WriteByte((byte)pos.Y);
            w.WriteInt(pos.Z);
            w.WriteByte((byte)mWorld.GetBlock(pos));
            w.WriteByte((byte)mWorld.GetMetadata(pos));
        });

    /// The item's own OnUse, run here rather than on the client: it fills buckets, lights TNT,
    /// hangs paintings and fires arrows, all of which are changes to the world or the inventory that
    /// the server owns. Player.UseHeldItem can't be reused - it reads GameContext's inventory, which
    /// on a server belongs to nobody in particular.
    private void UseHeldItem(ServerPlayer player, ItemStack stack, int heldIndex, Vector3i clicked, Vector3i? target)
    {
        var item = ItemRegistry.Get(stack.Item);

        if (item.IsFood)
        {
            if (player.Entity.Health >= Player.PLAYER_MAX_HEALTH)
                return;

            player.Entity.Health = Math.Min(player.Entity.Health + item.FoodRestore, Player.PLAYER_MAX_HEALTH);
            SendTo(player, PacketId.UpdateHealth, w => w.WriteShort((short)player.Entity.Health));

            player.Inventory.ConsumeOne(heldIndex);
            SendInventory(player);
            BroadcastHeldItem(player);
            return;
        }

        // Item code asks GameContext for "the" player and "the" inventory; on a server it has to be
        // told which, for exactly as long as this call takes.
        bool used;
        mContext.ActingPlayer = player;

        try
        {
            used = item.SkipBlockRaycast
                ? item.OnUse(mWorld, Vector3i.Zero, null)
                : target.HasValue && item.OnUse(mWorld, clicked, target);
        }
        finally
        {
            mContext.ActingPlayer = null;
        }

        if (!used)
            return;

        if (item.IsTool)
            player.Inventory.DamageTool(heldIndex);
        else
            player.Inventory.ConsumeOne(heldIndex);

        SendInventory(player);
        BroadcastHeldItem(player);
    }

    /// The same facing rules Player.Interaction uses - torches from the face that was clicked,
    /// everything else from where the player is looking, so it faces away from them.
    private static byte PlacementMetadata(ServerPlayer player, BlockType block, Vector3i clicked, Vector3i target)
    {
        if (block == BlockType.Torch)
        {
            var diff = target - clicked;

            if (diff.X == 1) return 4;                // west
            if (diff.X == -1) return 3;               // east
            if (diff.Z == 1) return 1;                // north
            if (diff.Z == -1) return 2;               // south

            return 0;                                 // stood on the floor
        }

        if (BlockRegistry.GetRenderType(block) != RenderingType.Stair &&
            block is not (BlockType.Furnace or BlockType.Chest))
            return 0;

        // Camera.Front's XZ is exactly ForwardFromYaw, so this is the client's own test.
        var front = ForwardFromYaw(player.Yaw);

        return MathF.Abs(front.X) > MathF.Abs(front.Z)
            ? front.X > 0 ? (byte)2 : (byte)3
            : front.Z > 0 ? (byte)1 : (byte)0;
    }

    /// One item out of the held slot, thrown a little way in front of them.
    private void DropHeldItem(ServerPlayer player)
    {
        int heldIndex = PlayerInventory.HOTBAR_START +
                        Math.Clamp((int)player.HeldSlot, 0, PlayerInventory.HOTBAR_SLOTS - 1);

        if (player.Inventory.GetSlot(heldIndex) is not { } held)
            return;

        player.Inventory.ConsumeOne(heldIndex);
        SendInventory(player);
        BroadcastHeldItem(player);

        // Thrown, not placed at your feet: the same arc the singleplayer path used.
        var forward = ForwardFromYaw(player.Yaw);
        var origin = player.Position + new Vector3(0f, 1.3f, 0f) + forward * 0.5f;

        SpawnDrop(origin, held.WithCount(1), player, forward * 5f + new Vector3(0f, 2f, 0f));
    }
}

// Client half of entity replication and block edits. | Stages 7-8

using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Infinite;
using VoxelEngine.UI;
using SilkKey = Silk.NET.Input.Key;

namespace VoxelEngine.Core;

public partial class Game
{
    // Other players, by server entity id. They also live in World.Entities so the normal render and
    // tick paths pick them up; this is just the lookup packets need.
    private readonly Dictionary<int, RemotePlayerEntity> mRemotePlayers = new();

    // Beta sent movement every tick. Cheap, and it's what makes chunks follow you.
    private void SendLocalPosition()
    {
        if (mNetwork == null || mPlayer == null)
            return;

        mNetwork.Send(PacketId.PlayerPositionLook, w =>
        {
            w.WriteDouble(mPlayer.Position.X);
            w.WriteDouble(mPlayer.Position.Y + mPlayer.EyeHeight);   // stance
            w.WriteDouble(mPlayer.Position.Y);
            w.WriteDouble(mPlayer.Position.Z);
            w.WriteFloat(mPlayer.Camera.Yaw);
            w.WriteFloat(mPlayer.Camera.Pitch);
            w.WriteBool(mPlayer.IsOnGround);
        });
    }

    // Blocks occupy 0-255 (BlockType tops out at 255), so items start above that. The server writes
    // these ids; we only read them.
    private const int ITEM_ID_BASE = 256;

    private static ItemStack? DecodeHeld(short id) =>
        id < 0 ? null
        : id < ITEM_ID_BASE ? ItemStack.FromBlock((BlockType)id)
        : ItemStack.FromItem((ItemType)(id - ITEM_ID_BASE));

    private int mLastSentSlot = -1;

    /// The server owns what is IN the slot - it only needs to know which slot, and it authors the
    /// EntityEquipment everyone else sees from that.
    private void SendHeldSlotIfChanged()
    {
        if (mNetwork == null || mHotbar == null)
            return;

        int slot = mHotbar.SelectedSlotIndex;
        if (slot == mLastSentSlot)
            return;

        mLastSentSlot = slot;
        mNetwork.Send(PacketId.HoldingChange, w => w.WriteShort((short)slot));
    }

    private void OnEntityEquipment(NetStream r)
    {
        int entityId = r.ReadInt();
        r.ReadShort();                            // slot; only the held slot exists for now
        short itemId = r.ReadShort();

        if (mRemotePlayers.TryGetValue(entityId, out var remote))
            remote.HeldItem = DecodeHeld(itemId);
    }

    /// When you swing, tell the server so everyone else sees it. No-op in singleplayer.
    private void SendSwing() =>
        mNetwork?.Send(PacketId.Animation, w =>
        {
            w.WriteInt(mNetwork.LocalEntityId);
            w.WriteByte(1);
        });

    // Wired in BuildNetworkSession. The local edit already happened; this reports it.
    private void HookLocalBlockEdits()
    {
        mPlayer.OnBlockBroken = pos => mNetwork?.Send(PacketId.PlayerDigging, w =>
        {
            w.WriteByte(2);                       // status 2 = finished; beta only acted on this
            w.WriteInt(pos.X);
            w.WriteByte((byte)pos.Y);
            w.WriteInt(pos.Z);
            w.WriteByte(1);                       // face, unused for digging
        });

        mPlayer.OnBlockBreakStarted = pos => mNetwork?.Send(PacketId.PlayerDigging, w =>
        {
            w.WriteByte(0);                       // status 0 = started digging
            w.WriteInt(pos.X);
            w.WriteByte((byte)pos.Y);
            w.WriteInt(pos.Z);
            w.WriteByte(1);
        });

        mPlayer.OnBlockPlaced = (clicked, target, block) => mNetwork?.Send(PacketId.PlayerBlockPlacement, w =>
        {
            // The CLICKED block plus a face, not the target - the server re-derives the target from
            // them, and sending the target directly would make it offset a second time.
            w.WriteInt(clicked.X);
            w.WriteByte((byte)clicked.Y);
            w.WriteInt(clicked.Z);
            w.WriteByte(FaceBetween(clicked, target));
            w.WriteShort((short)block);
            w.WriteByte(1);                       // stack count
            w.WriteShort(0);                      // damage
        });

        // Right-clicking a container: the server opens the window, and the screen follows from its
        // OpenWindow packet. Beta reused the placement packet for this.
        mPlayer.OnBlockUsed = clicked => mNetwork?.Send(PacketId.PlayerBlockPlacement, w =>
        {
            w.WriteInt(clicked.X);
            w.WriteByte((byte)clicked.Y);
            w.WriteInt(clicked.Z);
            w.WriteByte(1);                       // face; unused when the click opens a container
            w.WriteItem(null);                    // held item is the server's own record
        });
    }

    // Inverse of the server's FaceOffset.
    private static byte FaceBetween(Vector3i clicked, Vector3i target)
    {
        var d = target - clicked;

        if (d.Y == -1) return 0;
        if (d.Y == 1) return 1;
        if (d.Z == -1) return 2;
        if (d.Z == 1) return 3;
        if (d.X == -1) return 4;
        if (d.X == 1) return 5;

        // Placing into the clicked cell itself (a replaceable block like tall grass). Face 1 with a
        // zero delta would offset upward, so report the face whose offset lands back on the target.
        return 1;
    }

    // --- entity replication -------------------------------------------------------------------

    private void OnNamedEntitySpawn(NetStream r)
    {
        int entityId = r.ReadInt();
        string name = r.ReadString();
        var position = r.ReadFixedPos();
        float yaw = r.ReadAngle();
        float pitch = r.ReadAngle();
        r.ReadShort();                            // held item, applied by EntityEquipment

        if (entityId == mNetwork!.LocalEntityId || mRemotePlayers.ContainsKey(entityId))
            return;

        var remote = new RemotePlayerEntity(entityId, name, position, yaw, pitch);
        mRemotePlayers[entityId] = remote;
        mWorld.AddEntity(remote);
    }

    private void OnEntityTeleport(NetStream r)
    {
        int entityId = r.ReadInt();
        var position = r.ReadFixedPos();
        float yaw = r.ReadAngle();
        float pitch = r.ReadAngle();

        if (mRemotePlayers.TryGetValue(entityId, out var remote))
            remote.OnServerPosition(position, yaw, pitch);
        else if (mRemoteMobs.TryGetValue(entityId, out var mob))
            MoveMob(mob, position, yaw);
    }

    /// Mobs have no interpolation state of their own - Entity.TickProxy eases them toward this.
    /// The yaw arrives in degrees and a mob's own Yaw is radians (rendering rotates by it).
    private static void MoveMob(Entity mob, Vector3 position, float yawDegrees)
    {
        mob.NetTargetPosition = position;
        mob.Yaw = float.DegreesToRadians(yawDegrees);
    }

    /// EntityRelativeMove and EntityLookRelMove. Deltas are 1/32 of a block, signed.
    private void OnEntityRelativeMove(NetStream r, bool withLook)
    {
        int entityId = r.ReadInt();
        float dx = r.ReadSByte() / 32f;
        float dy = r.ReadSByte() / 32f;
        float dz = r.ReadSByte() / 32f;

        float yaw = 0f, pitch = 0f;
        if (withLook)
        {
            yaw = r.ReadAngle();
            pitch = r.ReadAngle();
        }

        if (mRemoteMobs.TryGetValue(entityId, out var mob))
        {
            MoveMob(mob, mob.NetTargetPosition + new Vector3(dx, dy, dz),
                    withLook ? yaw : float.RadiansToDegrees(mob.Yaw));
            return;
        }

        if (!mRemotePlayers.TryGetValue(entityId, out var remote))
            return;

        // Applied to the last SERVER position, not the interpolated one, or rounding error compounds.
        // ServerYaw is mesh-convention (negated) - undo that to get back the wire's camera-convention
        // degrees that OnServerPosition expects.
        var target = remote.ServerPosition + new Vector3(dx, dy, dz);
        remote.OnServerPosition(target,
            withLook ? yaw : -float.RadiansToDegrees(remote.ServerYaw),
            withLook ? pitch : float.RadiansToDegrees(remote.ServerPitch));
    }

    private void OnEntityLook(NetStream r)
    {
        int entityId = r.ReadInt();
        float yaw = r.ReadAngle();
        float pitch = r.ReadAngle();

        if (mRemotePlayers.TryGetValue(entityId, out var remote))
            remote.OnServerLook(yaw, pitch);
        else if (mRemoteMobs.TryGetValue(entityId, out var mob))
            mob.Yaw = float.DegreesToRadians(yaw);
    }

    private void OnDestroyEntity(NetStream r)
    {
        int entityId = r.ReadInt();

        if (!mRemotePlayers.Remove(entityId, out var remote))
        {
            RemoveRemoteMob(entityId);
            return;
        }

        mWorld.RemoveEntity(remote);
    }

    private void OnEntityAnimation(NetStream r)
    {
        int entityId = r.ReadInt();
        byte animation = r.ReadByte();            // 1 = swing, 2 = hurt (Stage 11)

        // Only the swing is drawn; without the check, being hit made everyone else swing their arm.
        if (animation == 1 && mRemotePlayers.TryGetValue(entityId, out var remote))
            remote.OnSwingArm();
    }

    // --- block changes ------------------------------------------------------------------------

    // Changes for chunks that haven't been integrated yet, replayed when they land. Without this
    // they write into a chunk that doesn't exist, SetBlockDirect silently no-ops, and the change is
    // lost for good - the classic "my friend's build looks different on my screen".
    private readonly Dictionary<ChunkCoord, List<(Vector3i pos, BlockType type, byte meta)>> mDeferredBlocks = new();
    private int mDeferredBlockCount;

    private const int MAX_DEFERRED_BLOCKS = 4096;

    private void OnBlockChange(NetStream r)
    {
        int x = r.ReadInt();
        int y = r.ReadByte();
        int z = r.ReadInt();
        var type = (BlockType)r.ReadByte();
        byte metadata = r.ReadByte();

        ReceiveBlockChange(x, y, z, type, metadata);
    }

    /// One server-authored block change, applied now or when its chunk arrives. Shared with the
    /// batched MultiBlockChange path.
    private void ReceiveBlockChange(int x, int y, int z, BlockType type, byte metadata)
    {
        var coord = ChunkCoord.FromWorldBlock(x, z);

        // "Sent" only means the packet left the server. On this end chunk data waits in
        // NetworkChunkSource's queue and integrates a couple per frame, so it may not be here yet.
        if (mWorld.Streamer.GetChunk(coord.X, coord.Z) == null)
        {
            if (mDeferredBlockCount >= MAX_DEFERRED_BLOCKS)
                return;                       // a client that never integrates must not grow this forever

            if (!mDeferredBlocks.TryGetValue(coord, out var pending))
                mDeferredBlocks[coord] = pending = new();

            pending.Add((new Vector3i(x, y, z), type, metadata));
            mDeferredBlockCount++;
            return;
        }

        ApplyBlockChange(x, y, z, type, metadata);
    }

    /// From InfiniteWorldStreamer the moment a chunk becomes resident.
    private void OnChunkIntegrated(ChunkCoord coord)
    {
        if (!mDeferredBlocks.Remove(coord, out var pending))
            return;

        mDeferredBlockCount -= pending.Count;

        // Replayed in arrival order, so the last write per position wins - as it did on the server.
        foreach (var (pos, type, meta) in pending)
            ApplyBlockChange(pos.X, pos.Y, pos.Z, type, meta);
    }

    // Not SetBlock: the server already ran the placement rules and neighbour ticks and will send
    // anything that fell out of them. Lighting IS recomputed here - the server sends block changes,
    // not light levels, so without it a broken block leaves its shadow behind.
    private void ApplyBlockChange(int x, int y, int z, BlockType type, byte metadata) =>
        mWorld.SetBlockFromServer(x, y, z, type, metadata);

    // Dropped when the world goes away, or they'd be re-added to the next session's world.
    private void ClearRemotePlayers()
    {
        mRemotePlayers.Clear();
        mRemoteMobs.Clear();
    }

    // --- overlays -----------------------------------------------------------------------------

    /// Chat, name tags and the Tab list. All no-ops in singleplayer.
    private void RenderMultiplayerOverlays(float dt)
    {
        if (mNetwork == null)
            return;

        NameTagRenderer.Render(mRemotePlayers.Values, mPlayer.Camera,
                               new Vector2(mWindow.Size.X, mWindow.Size.Y));

        mChatScreen.Render(dt);

        // Held, not toggled - matches every other game's Tab list.
        if (IsKeyDown(SilkKey.Tab))
            PlayerListOverlay.Render(PlayerName, mRemotePlayers.Values.Select(p => p.Name));
    }

    private void OnChatMessage(NetStream r) => mChatScreen.AddMessage(r.ReadString());

    // --- windows -------------------------------------------------------------------------------

    /// The server's mirror of whatever container is open. Null-safe in singleplayer: the screens
    /// only consult it when Network != null.
    public ClientWindows Windows { get; } = new();

    /// True while connected to a server. The screens use it to choose local vs server-owned state.
    public bool IsMultiplayer => mNetwork != null;

    internal ClientNetwork? Network => mNetwork;

    private void OnOpenWindow(NetStream r)
    {
        byte id = r.ReadByte();
        var kind = (WindowKind)r.ReadByte();
        string title = r.ReadString();
        r.ReadByte();                             // container slot count - WindowLayout knows it

        Windows.OnOpenWindow(id, kind, title);
        OpenScreenFor(kind);
    }

    private void OnWindowItems(NetStream r)
    {
        byte id = r.ReadByte();
        int count = r.ReadShort();

        var slots = new ItemStack?[count];
        for (int i = 0; i < count; i++)
            slots[i] = r.ReadItem();

        Windows.OnWindowItems(id, slots);

        if (id == Windows.CurrentId)
            MirrorInventory(slots);
    }

    private void OnSetSlot(NetStream r)
    {
        byte id = r.ReadByte();
        short slot = r.ReadShort();
        Windows.OnSetSlot(id, slot, r.ReadItem());
    }

    private void OnProgressBar(NetStream r)
    {
        byte id = r.ReadByte();
        short bar = r.ReadShort();
        Windows.OnProgressBar(id, bar, r.ReadShort());
    }

    /// The hotbar, the held item and the arm all read PlayerInventory, not the snapshot, so the
    /// inventory half of every snapshot is copied back into it.
    private void MirrorInventory(ItemStack?[] slots)
    {
        if (mInventory is not { } inv)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            int index = WindowLayout.ToInventoryIndex(Windows.CurrentKind, i);
            if (index >= 0)
                inv.SetSlot(index, slots[i]);
        }
    }

    // The server decides which container opened; the client just shows the matching screen. No
    // block entity is created - the client has no container data of its own any more.
    private void OpenScreenFor(WindowKind kind)
    {
        CurrentState = kind switch
        {
            WindowKind.Chest => GameState.Chest,
            WindowKind.DoubleChest => GameState.DoubleChest,
            WindowKind.Furnace => GameState.Furnace,
            WindowKind.Workbench => GameState.Crafting,
            _ => CurrentState,
        };

        SetCursorGrabbed(false);
        mMouse.Position = new Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
    }

    /// Screens call this on close so the server can drop the cursor stack into the world.
    internal void CloseServerWindow()
    {
        if (mNetwork != null && Windows.IsOpen)
            Windows.SendClose(mNetwork);
    }

    private void SendChatMessage(string text) =>
        mNetwork?.Send(PacketId.ChatMessage, w => w.WriteString(text));
}

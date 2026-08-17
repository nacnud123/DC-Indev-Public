// Open, click, close. The server owns every slot; clients render what they're sent. | Stage 10

using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private readonly Dictionary<byte, WindowSession> mWindows = new();
    private byte mLastWindowId;

    /// Called when a player right-clicks a container block.
    internal void OpenWindow(ServerPlayer player, WindowKind kind, Vector3i pos, string title)
    {
        byte id = AllocateWindowId();

        var session = new WindowSession
        {
            Id = id,
            Kind = kind,
            BlockPosition = pos,
            Viewer = player,
            Workbench = kind == WindowKind.Workbench ? new CraftingGrid(3, 3) : null,
        };

        mWindows[id] = session;
        player.OpenWindowId = id;

        SendTo(player, PacketId.OpenWindow, w =>
        {
            w.WriteByte(id);
            w.WriteByte((byte)kind);
            w.WriteString(title);
            w.WriteByte((byte)WindowLayout.ContainerSlotCount(kind));
        });

        SendSnapshot(session);
    }

    /// True if the clicked block is a container and a window was opened for it, in which case the
    /// placement that carried us here must not also happen.
    internal bool TryOpenContainer(ServerPlayer player, Vector3i pos)
    {
        var (kind, title) = mWorld.GetBlock(pos) switch
        {
            BlockType.Chest => (WindowKind.Chest, "Chest"),
            BlockType.DoubleChest => (WindowKind.DoubleChest, "Large Chest"),
            BlockType.Furnace or BlockType.FurnaceLit => (WindowKind.Furnace, "Furnace"),
            BlockType.WorkBench => (WindowKind.Workbench, "Crafting"),
            _ => (default(WindowKind), ""),
        };

        if (title.Length == 0)
            return false;

        // Sneaking places against a container instead of opening it, as it does in Minecraft.
        OpenWindow(player, kind, pos, title);
        return true;
    }

    private void HandleWindowClick(ServerPlayer player, NetStream r)
    {
        byte windowId = r.ReadByte();
        short slot = r.ReadShort();
        bool rightClick = r.ReadByte() != 0;
        r.ReadShort();                                 // action number - Beta's rollback id, unused
        bool shift = r.ReadByte() != 0;
        r.ReadItem();                                  // the client's idea of the slot; not trusted

        var session = SessionFor(player, windowId);
        if (session == null)
            return;

        // b1.7.3's Container.isUsableByPlayer, which this had no equivalent of: only opening a
        // container was reach-checked, so a player could open a chest, walk to the other side of
        // the world, and go on taking things out of it.
        if (!WithinContainerReach(player, session))
        {
            SendTo(player, PacketId.CloseWindow, w => w.WriteByte(windowId));
            CloseWindow(player, windowId);
            return;
        }

        // Shift wins over the button, matching beta: shift-right-click is a move, not a half-take.
        var click = (rightClick, shift) switch
        {
            (_, true) => ClickType.ShiftLeftClick,
            (true, false) => ClickType.RightClick,
            (false, false) => ClickType.LeftClick,
        };

        // The result slot isn't a normal slot - you can take from it but never put into it, and
        // taking consumes the ingredients. It has to be handled before the generic click path, and
        // it must NOT write a composite back afterwards: TakeResult already emptied the grid, and
        // the composite was built before that, so writing it back would restore the ingredients.
        if (slot == WindowLayout.ResultSlot(session.Kind) && session.Grid is { } grid)
        {
            TakeCraftResult(player, session, grid, shift);
        }
        else
        {
            var slots = session.BuildComposite(mWorld);
            player.Cursor = InventoryClick.Apply(slots, slot, click, player.Cursor);
            session.WriteBack(mWorld, slots);
        }

        BroadcastSnapshot(session);
        SendCursor(player);
    }

    /// b1.7.3 allowed 8 blocks from the container's centre. The player's own inventory and a
    /// workbench grid have no block behind them, so they are always in reach.
    private const float CONTAINER_REACH = 8f;

    private static bool WithinContainerReach(ServerPlayer player, WindowSession session)
    {
        if (session.Kind is WindowKind.PlayerInventory or WindowKind.Workbench)
            return true;

        var centre = session.BlockPosition.ToVector3() + new Vector3(0.5f, 0.5f, 0.5f);
        return (player.Position - centre).LengthSquared() <= CONTAINER_REACH * CONTAINER_REACH;
    }

    private void TakeCraftResult(ServerPlayer player, WindowSession session, CraftingGrid grid, bool shift)
    {
        // TakeResult returns null when no recipe matches, so a client asking for a diamond by
        // claiming a match gets nothing.
        if (grid.TakeResult() is not { } crafted)
            return;

        if (shift || player.Cursor != null)
        {
            // Shift-craft, or a full cursor: straight to the inventory, dropped if it won't fit.
            if (player.Inventory.AddGetRemainder(crafted) is { } leftover)
                SpawnDrop(player.Position, leftover, player);
        }
        else
        {
            player.Cursor = crafted;
        }
    }

    private void HandleCloseWindow(ServerPlayer player, NetStream r)
    {
        byte id = r.ReadByte();
        CloseWindow(player, id);
    }

    internal void CloseWindow(ServerPlayer player, byte id)
    {
        // Whatever is on the cursor falls into the world, as Beta did - otherwise closing a window
        // holding a stack silently deletes it.
        if (player.Cursor is { } held)
        {
            SpawnDrop(player.Position, held, player);
            player.Cursor = null;
            SendCursor(player);
        }

        // Ingredients left in a crafting grid go back to the player, or they're lost with the window.
        if (mWindows.TryGetValue(id, out var session) && session.Grid is { } grid)
        {
            foreach (var stack in grid.Slots)
                if (stack is { } s && player.Inventory.AddGetRemainder(s) is { } leftover)
                    SpawnDrop(player.Position, leftover, player);

            for (int i = 0; i < grid.Slots.Length; i++)
                grid.SetSlot(i, null);
        }

        if (id != 0)
            mWindows.Remove(id);

        player.OpenWindowId = 0;

        // Window 0 is what's open once this one isn't, and the client cleared its mirror on close.
        SendInventory(player);
    }

    /// Window 0 is the player's own inventory and is never in the dictionary - it's always open.
    private WindowSession? SessionFor(ServerPlayer player, byte windowId)
    {
        if (windowId == 0)
            return player.InventoryWindow;

        var session = mWindows.GetValueOrDefault(windowId);
        return session?.Viewer == player ? session : null;   // never let one player drive another's
    }

    /// The whole window, to one viewer.
    private void SendSnapshot(WindowSession session)
    {
        var slots = session.BuildComposite(mWorld);

        SendTo(session.Viewer, PacketId.WindowItems, w =>
        {
            w.WriteByte(session.Id);
            w.WriteShort((short)slots.Length);
            foreach (var s in slots) w.WriteItem(s);
        });
    }

    /// Same container, every viewer. Each gets their OWN composite - the container half matches but
    /// the inventory half is per-player, so this can't be one buffer sent to everyone.
    private void BroadcastSnapshot(WindowSession session)
    {
        if (session.Kind is WindowKind.PlayerInventory or WindowKind.Workbench)
        {
            SendSnapshot(session);                     // private to one player either way
            return;
        }

        SendSnapshot(session);

        foreach (var other in mWindows.Values)
            if (other != session && other.BlockPosition == session.BlockPosition && other.Kind == session.Kind)
                SendSnapshot(other);
    }

    private void SendCursor(ServerPlayer player) =>
        SendTo(player, PacketId.SetSlot, w =>
        {
            w.WriteByte(ClientWindows.CURSOR_WINDOW);
            w.WriteShort(ClientWindows.CURSOR_SLOT);
            w.WriteItem(player.Cursor);
        });

    /// Pushes the player's own inventory to them - after a pickup, a dig drop, or a placement.
    internal void SendInventory(ServerPlayer player) => SendSnapshot(player.InventoryWindow);

    /// What everyone else sees in this player's hand. Authored here, not relayed from the client:
    /// the server knows both the held slot and what's in it, and two sources of truth for the same
    /// fact is how held items end up wrong for everyone but the holder.
    internal void BroadcastHeldItem(ServerPlayer player) =>
        BroadcastExcept(player, PacketId.EntityEquipment, w => WriteHeldItem(w, player));

    /// Same payload as BroadcastHeldItem, but to one recipient - used to backfill a newcomer with
    /// held items for players who were already here and won't switch slots again on their own.
    internal void SendHeldItem(ServerPlayer to, ServerPlayer subject) =>
        SendTo(to, PacketId.EntityEquipment, w => WriteHeldItem(w, subject));

    private static void WriteHeldItem(NetStream w, ServerPlayer player)
    {
        int slot = PlayerInventory.HOTBAR_START + Math.Clamp((int)player.HeldSlot, 0, PlayerInventory.HOTBAR_SLOTS - 1);
        var held = player.Inventory.GetSlot(slot);

        w.WriteInt(player.EntityId);
        w.WriteShort(0);                           // slot 0 = held
        w.WriteShort(held is { } s ? (s.IsBlock ? (short)s.Block : (short)(256 + (int)s.Item)) : (short)-1);
    }

    private byte AllocateWindowId()
    {
        for (int i = 0; i < 127; i++)
        {
            mLastWindowId = (byte)(mLastWindowId % 127 + 1);
            if (!mWindows.ContainsKey(mLastWindowId))
                return mLastWindowId;
        }

        return 1;                                      // 127 windows open at once isn't a real case
    }

    /// Every window onto this block, closed because the block is being destroyed. Anyone still
    /// looking at it would otherwise go on clicking slots of a container that no longer exists -
    /// and GetOrCreateChest would quietly resurrect it under them.
    internal void CloseWindowsAt(Vector3i pos)
    {
        foreach (var (id, session) in mWindows.Where(kv => kv.Value.BlockPosition == pos).ToList())
        {
            SendTo(session.Viewer, PacketId.CloseWindow, w => w.WriteByte(id));
            CloseWindow(session.Viewer, id);
        }
    }

    /// Every window this player has open, closed because they're leaving.
    internal void CloseAllWindows(ServerPlayer player)
    {
        foreach (var id in mWindows.Where(kv => kv.Value.Viewer == player).Select(kv => kv.Key).ToList())
            CloseWindow(player, id);
    }
}

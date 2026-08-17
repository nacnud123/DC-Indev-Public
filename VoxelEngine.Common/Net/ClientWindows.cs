// The client's mirror of whatever container window is open. Render-only. | Stage 10

using VoxelEngine.Items;

namespace VoxelEngine.Net;

/// <summary>
/// Every field here is written by a packet and read by a screen; nothing else writes them. A click
/// is a request - the client changes nothing locally and the next snapshot is the answer, so there
/// is no pending list and no rollback.
/// </summary>
public sealed class ClientWindows
{
    public byte CurrentId { get; private set; }
    public WindowKind CurrentKind { get; private set; }
    public string Title { get; private set; } = "";
    public ItemStack?[] Slots { get; private set; } = Array.Empty<ItemStack?>();
    public ItemStack? Cursor { get; private set; }

    public bool IsOpen => Slots.Length > 0;

    // Furnace meters. Nothing smelts on the client any more, so these only ever arrive by packet.
    public short CookProgress { get; private set; }
    public short BurnTime { get; private set; }
    public short BurnMax { get; private set; }

    public const short BAR_COOK = 0;
    public const short BAR_BURN = 1;
    public const short BAR_BURN_MAX = 2;

    public void OnProgressBar(byte id, short bar, short value)
    {
        if (id != CurrentId)
            return;

        switch (bar)
        {
            case BAR_COOK: CookProgress = value; break;
            case BAR_BURN: BurnTime = value; break;
            case BAR_BURN_MAX: BurnMax = value; break;
        }
    }

    public void OnOpenWindow(byte id, WindowKind kind, string title)
    {
        CurrentId = id;
        CurrentKind = kind;
        Title = title;

        // Sized now so a screen opened before the first snapshot renders empty rather than throwing.
        Slots = new ItemStack?[WindowLayout.TotalSlots(kind)];
    }

    public void OnWindowItems(byte id, ItemStack?[] slots)
    {
        if (id != CurrentId)
            return;                                   // a stale snapshot for a window we already closed

        Slots = slots;
    }

    /// Only ever the cursor - window 255, slot -1. Container contents arrive as whole snapshots.
    public void OnSetSlot(byte id, short slot, ItemStack? stack)
    {
        if (id == CURSOR_WINDOW && slot == CURSOR_SLOT)
            Cursor = stack;
    }

    public void Close()
    {
        Slots = Array.Empty<ItemStack?>();
        Cursor = null;
        CurrentId = 0;
        CurrentKind = WindowKind.PlayerInventory;   // window 0 is what's open once anything else isn't
        CookProgress = BurnTime = BurnMax = 0;
    }

    public ItemStack? SlotAt(int index) =>
        index >= 0 && index < Slots.Length ? Slots[index] : null;

    /// Asks the server to apply a click. Nothing changes here until the snapshot comes back.
    public void SendClick(int slot, ClickType click, ClientNetwork network) =>
        network.Send(PacketId.WindowClick, w =>
        {
            w.WriteByte(CurrentId);
            w.WriteShort((short)slot);
            w.WriteByte(click == ClickType.RightClick ? (byte)1 : (byte)0);
            w.WriteShort(0);                          // action number - unused, kept for Beta's shape
            w.WriteByte(click is ClickType.ShiftLeftClick or ClickType.ShiftRightClick ? (byte)1 : (byte)0);
            w.WriteItem(SlotAt(slot));
        });

    public void SendClose(ClientNetwork network)
    {
        network.Send(PacketId.CloseWindow, w => w.WriteByte(CurrentId));
        Close();
    }

    /// Beta's convention for "this is the stack on the mouse" rather than a slot in a window.
    public const byte CURSOR_WINDOW = 255;
    public const short CURSOR_SLOT = -1;
}

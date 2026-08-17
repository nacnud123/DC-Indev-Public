// Smelting happens here; clients are sent the slots and the two meters. | Stage 10

using VoxelEngine.BlockEntities;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    // What clients were last told, so a furnace that isn't doing anything costs nothing to keep.
    // The lit/unlit block swap needs nothing here - it goes through the change journal.
    private readonly Dictionary<Vector3i, (ItemStack? In, ItemStack? Fuel, ItemStack? Out, bool Lit)> mFurnaceState = new();

    private void TickFurnaces()
    {
        BlockEntityManager.TickFurnaces();

        // A broken furnace leaves its last-sent snapshot behind, and nothing else ever removes it.
        if (mFurnaceState.Count > 0)
        {
            var live = BlockEntityManager.Furnaces.Select(f => f.Position).ToHashSet();

            foreach (var pos in mFurnaceState.Keys.Where(p => !live.Contains(p)).ToList())
                mFurnaceState.Remove(pos);
        }

        foreach (var furnace in BlockEntityManager.Furnaces)
        {
            var now = (In: furnace.InputSlot, Fuel: furnace.FuelSlot, Out: furnace.OutputSlot, Lit: furnace.IsLit);
            mFurnaceState.TryGetValue(furnace.Position, out var before);
            mFurnaceState[furnace.Position] = now;

            if (!SameStack(before.In, now.In) ||
                !SameStack(before.Fuel, now.Fuel) ||
                !SameStack(before.Out, now.Out))
            {
                foreach (var session in FurnaceWindowsAt(furnace.Position))
                    SendSnapshot(session);
            }
        }

        // The meters move every tick while lit, so they go out every tick a furnace window is open.
        foreach (var session in mWindows.Values)
        {
            if (session.Kind != WindowKind.Furnace)
                continue;

            var furnace = BlockEntityManager.GetOrCreateFurnace(session.BlockPosition);

            SendBar(session, ClientWindows.BAR_COOK, furnace.SmeltProgress);
            SendBar(session, ClientWindows.BAR_BURN, furnace.BurnTimeRemaining);
            SendBar(session, ClientWindows.BAR_BURN_MAX, furnace.CurrentFuelMax);
        }
    }

    private IEnumerable<WindowSession> FurnaceWindowsAt(Vector3i pos) =>
        mWindows.Values.Where(s => s.Kind == WindowKind.Furnace && s.BlockPosition == pos);

    private void SendBar(WindowSession session, short bar, int value) =>
        SendTo(session.Viewer, PacketId.UpdateProgressBar, w =>
        {
            w.WriteByte(session.Id);
            w.WriteShort(bar);
            w.WriteShort((short)Math.Clamp(value, 0, short.MaxValue));
        });

    /// ItemStack equality deliberately ignores count, which is exactly the change we're watching for.
    private static bool SameStack(ItemStack? a, ItemStack? b) =>
        (a, b) switch
        {
            (null, null) => true,
            ({ } x, { } y) => x == y && x.Count == y.Count,
            _ => false,
        };
}

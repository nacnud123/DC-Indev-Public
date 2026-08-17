// Dropped items. The server owns spawning and pickup entirely. | Stage 10

using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private const float PICKUP_RADIUS = 2f;
    private const int PICKUP_DELAY_TICKS = 10;         // stops you instantly re-collecting a throw

    /// Called once per tick. DroppedItemEntity's own pickup branch is guarded on there being a local
    /// player, so on a server it does nothing and this is the only pickup path.
    private void TickDroppedItems()
    {
        foreach (var drop in mWorld.Entities.OfType<DroppedItemEntity>().ToList())
        {
            // DroppedItemEntity despawns itself after MAX_AGE, and World.TickEntities drops it
            // silently - without this the clients keep a phantom item lying there forever.
            // TrackDropsFor sends the DestroyEntity, to the players who were actually shown it.
            if (!drop.IsAlive)
            {
                mDropAges.Remove(drop.Id);
                continue;
            }

            if (!mDropAges.TryGetValue(drop.Id, out int age))
                age = 0;

            mDropAges[drop.Id] = age + 1;

            if (age < PICKUP_DELAY_TICKS)
                continue;

            foreach (var player in mPlayers)
            {
                // A corpse lying in its own drops must not collect them - that would hand the whole
                // inventory straight back and make dying free.
                if (player.IsDead)
                    continue;

                if ((player.Position - drop.Position).LengthSquared() > PICKUP_RADIUS * PICKUP_RADIUS)
                    continue;

                var leftover = player.Inventory.AddGetRemainder(drop.Stack);

                if (leftover is { } rest && rest.Count == drop.Stack.Count)
                    continue;                          // full inventory: leave it lying there

                SendInventory(player);                 // their inventory changed

                // Only the players who were shown this drop: to anyone else the id means nothing.
                foreach (var viewer in mPlayers.Where(v => v.TrackedDrops.Contains(drop.Id)))
                {
                    SendTo(viewer, PacketId.CollectItem, w =>
                    {
                        w.WriteInt(drop.Id);
                        w.WriteInt(player.EntityId);
                    });
                }

                drop.IsAlive = false;
                mDropAges.Remove(drop.Id);                 // TrackDropsFor sends the DestroyEntity

                // Only part of it fit. Beta has no "change a drop's stack size" packet, so the
                // remainder goes back as a fresh entity - testing TryAdd's bool here deleted it
                // instead, and walking over 64 blocks with room for 10 threw the other 54 away.
                if (leftover is { } remainder)
                    SpawnDrop(drop.Position, remainder, player);

                break;
            }
        }
    }

    // Age lives here rather than on the entity: DroppedItemEntity's own age drives despawn and the
    // client's bob animation, and pickup delay is a server-only concept.
    private readonly Dictionary<int, int> mDropAges = new();

    /// Spawns a drop and tells everyone. `thrower` is excluded from nothing - they see it too.
    /// `velocity` is the throw: without it a dropped item lands on your own feet.
    internal void SpawnDrop(Vector3 position, ItemStack stack, ServerPlayer? thrower, Vector3 velocity = default)
    {
        var drop = new DroppedItemEntity(position, stack) { Velocity = velocity };
        drop.AssignNetworkId(Entity.AllocateId());
        drop.LastSentPosition = position;
        mWorld.AddEntity(drop);

        // No broadcast: TrackDropsFor spawns it, this tick, for the players who hold its chunk.
        // Broadcasting sent every drop in the world to everyone, so a player mining alone in a cave
        // pushed a packet to every other player for each block, and clients ended up with items
        // lying in chunks they had never been sent.
    }

    /// The enter/leave machinery mobs use (TrackMobsFor), for drops. The audience is whoever holds
    /// the chunk the item is in - the same test BroadcastEntityMovement already uses for their
    /// movement, so a client can no longer be sent a move for an entity it was never shown.
    private void TrackDropsFor(ServerPlayer player)
    {
        var visible = mWorld.Entities.OfType<DroppedItemEntity>()
            .Where(d => d.IsAlive && player.SentChunks.Contains(ChunkCoord.FromWorldBlock(
                (int)MathF.Floor(d.Position.X), (int)MathF.Floor(d.Position.Z))))
            .ToDictionary(d => d.Id);

        foreach (var (id, drop) in visible)
        {
            if (!player.TrackedDrops.Add(id))
                continue;

            SendTo(player, PacketId.PickupSpawn, w =>
            {
                w.WriteInt(drop.Id);
                w.WriteShort(EncodeItemId(drop.Stack));
                w.WriteByte((byte)Math.Clamp(drop.Stack.Count, 0, 255));
                w.WriteShort((short)Math.Max(drop.Stack.Durability, 0));
                w.WriteFixedPos(drop.Position);
                w.WriteByte(0);                        // rotation, pitch, roll - unused by our renderer
                w.WriteByte(0);
                w.WriteByte(0);
            });
        }

        foreach (var id in player.TrackedDrops.Where(id => !visible.ContainsKey(id)).ToList())
        {
            SendTo(player, PacketId.DestroyEntity, w => w.WriteInt(id));
            player.TrackedDrops.Remove(id);
        }
    }

    /// mDropAges is keyed by entity id and nothing removes an entry for a drop that leaves the
    /// world without dying first - an unloaded chunk, a server restart mid-flight - so it grows for
    /// as long as the server runs.
    private void PruneDropAges()
    {
        if (mDropAges.Count == 0)
            return;

        var live = mWorld.Entities.OfType<DroppedItemEntity>().Select(d => d.Id).ToHashSet();

        foreach (var id in mDropAges.Keys.Where(id => !live.Contains(id)).ToList())
            mDropAges.Remove(id);
    }

    // Same encoding NetStream.WriteItem uses; PickupSpawn splits the fields rather than sending a slot.
    private static short EncodeItemId(ItemStack stack) =>
        stack.IsBlock ? (short)stack.Block : (short)(256 + (int)stack.Item);
}

// Mobs live here and are mirrored to clients. | Stage 11

using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Net;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private const float MOB_TRACK_RANGE = 64f;
    private const float MOB_UNTRACK_RANGE = 72f;

    /// Past this a mob is nobody's problem. Mobs that wander out of the loaded chunks have no
    /// ground to stand on and fall through the world, which is what "mobs behaving strangely" at a
    /// distance actually is.
    private const float MOB_DESPAWN_RANGE = 160f;

    private MobSpawner? mMobSpawner;

    /// Spawning is the server's alone - a client that also spawned would fill the world with mobs
    /// only it can see. Nothing spawns with nobody online: MobSpawner needs a player to spawn around.
    private void TickMobSpawning()
    {
        if (!mProps.SpawnMonsters || mPlayers.Count == 0)
            return;

        mMobSpawner ??= new MobSpawner(mWorld, Random.Shared);
        mMobSpawner.Tick();
        DespawnDistantMobs();
    }

    /// Anything the world created for itself - a spawned mob, an arrow, a lit TNT - still has its
    /// constructor's id, which comes from a different counter than the server's. Hand out proper
    /// ids before anything is replicated, or two entities end up sharing one.
    private void AdoptNewEntities()
    {
        foreach (var entity in mWorld.Entities)
        {
            if (!entity.HasNetworkId)
                entity.AssignNetworkId(Entity.AllocateId());
        }
    }

    private void DespawnDistantMobs()
    {
        foreach (var mob in TrackableMobs().ToList())
        {
            if (mob is PaintingEntity)
                continue;                              // part of the world, not a spawned mob

            bool nearSomeone = mPlayers.Any(p => (p.Position - mob.Position).Length() < MOB_DESPAWN_RANGE);
            var coord = ChunkCoord.FromWorldBlock((int)MathF.Floor(mob.Position.X),
                                                  (int)MathF.Floor(mob.Position.Z));
            bool chunkLoaded = mWorld.Streamer.GetChunk(coord.X, coord.Z) != null;

            if (nearSomeone && chunkLoaded)
                continue;

            // TrackMobsFor sends the DestroyEntity: it prunes any id that is no longer in range.
            mWorld.RemoveEntity(mob);
        }
    }

    /// Everything that isn't a player uses the same enter/leave machinery as players (Stage 7);
    /// only the spawn packet differs by kind.
    private void TrackMobsFor(ServerPlayer player)
    {
        // Two ranges, not one: a mob standing exactly on the boundary would otherwise be spawned and
        // destroyed on alternate ticks, which on the client is a mob that flickers in and out.
        var tracked = TrackableMobs()
            .Where(e => (e.Position - player.Position).Length() <
                        (player.TrackedEntities.Contains(e.Id) ? MOB_UNTRACK_RANGE : MOB_TRACK_RANGE))
            .ToDictionary(e => e.Id);

        foreach (var (id, entity) in tracked)
        {
            if (!player.TrackedEntities.Add(id))
                continue;

            SendSpawn(player, entity);
        }

        // Players are tracked by the login/spawn path, so only mob ids are pruned here.
        foreach (var id in player.TrackedEntities.Where(id => !tracked.ContainsKey(id) && IsMobId(id)).ToList())
        {
            SendTo(player, PacketId.DestroyEntity, w => w.WriteInt(id));
            player.TrackedEntities.Remove(id);
        }
    }

    /// One spawn packet, chosen by what the entity is. Beta had three: living things, "objects"
    /// (arrows, primed TNT, boats...) and paintings, which carry their art instead of a position.
    private void SendSpawn(ServerPlayer player, Entity entity)
    {
        if (entity is PaintingEntity painting)
        {
            SendTo(player, PacketId.EntityPainting, w =>
            {
                w.WriteInt(painting.Id);
                w.WriteString(painting.Art.Name);
                w.WriteInt(painting.AnchorPos.X);
                w.WriteInt(painting.AnchorPos.Y);
                w.WriteInt(painting.AnchorPos.Z);
                w.WriteInt(painting.Facing);
            });

            return;
        }

        if (ObjectTypeIdOf(entity) is var objectType and not 0)
        {
            SendTo(player, PacketId.AddObject, w =>
            {
                w.WriteInt(entity.Id);
                w.WriteByte(objectType);
                w.WriteFixedPos(entity.Position);
                w.WriteInt(0);                         // thrower id; 0 means no velocity tail
            });

            return;
        }

        SendTo(player, PacketId.MobSpawn, w =>
        {
            w.WriteInt(entity.Id);
            w.WriteByte(MobTypeIdOf(entity));
            w.WriteFixedPos(entity.Position);
            w.WriteAngle(float.RadiansToDegrees(entity.Yaw));
            w.WriteByte(0);                            // pitch
            w.WriteByte(0x7F);                         // empty metadata stream - terminator only
        });
    }

    /// Movement for everything the server owns that isn't a player: mobs, to whoever tracks them,
    /// and dropped items, to whoever holds the chunk they're in. Without the second half a thrown
    /// item lands on the server and hangs in the air on every client.
    private void BroadcastEntityMovement()
    {
        foreach (var mob in TrackableMobs())
        {
            if (mob is PaintingEntity)
                continue;                              // hung on a wall; it never moves

            SendMovement(mob, float.RadiansToDegrees(mob.Yaw), viewer => viewer.TrackedEntities.Contains(mob.Id));
        }

        foreach (var drop in mWorld.Entities.OfType<DroppedItemEntity>())
        {
            if (!drop.IsAlive)
                continue;

            var coord = ChunkCoord.FromWorldBlock((int)MathF.Floor(drop.Position.X),
                                                  (int)MathF.Floor(drop.Position.Z));

            SendMovement(drop, 0f, viewer => viewer.SentChunks.Contains(coord));
        }
    }

    /// One entity's movement, as a relative step or an absolute resync.
    ///
    /// Two things here are easy to get wrong and both look like "entities behave weirdly":
    /// TicksSinceUpdate has to advance every tick, not only on the ticks where the entity stood
    /// still, or something that never stops moving never gets its periodic resync; and the delta
    /// folded back into LastSentPosition has to be the QUANTISED one that was actually sent, or the
    /// 1/32-block truncation accumulates and the client's copy drifts away from the server's.
    /// <param name="yawDegrees">
    /// Explicit because the two kinds of entity disagree: a mob's <c>Entity.Yaw</c> is RADIANS (its
    /// AI writes an Atan2 straight into it, and rendering rotates by it), while a player's yaw comes
    /// from the client's camera in degrees. The wire is degrees.
    /// </param>
    internal void SendMovement(Entity entity, float yawDegrees, Func<ServerPlayer, bool> audience)
    {
        var delta = entity.Position - entity.LastSentPosition;
        entity.TicksSinceUpdate++;

        // Quantise FIRST. A resting entity that drifts by less than 1/32 of a block never clears its
        // delta, so testing the raw delta sends a zero-movement packet every tick, forever.
        sbyte dx = (sbyte)(delta.X * 32), dy = (sbyte)(delta.Y * 32), dz = (sbyte)(delta.Z * 32);
        bool moved = dx != 0 || dy != 0 || dz != 0;

        if (!moved && entity.TicksSinceUpdate < 20)
            return;

        bool small = moved && entity.TicksSinceUpdate < 20 &&
                     MathF.Abs(delta.X) < 3.9f && MathF.Abs(delta.Y) < 3.9f && MathF.Abs(delta.Z) < 3.9f;

        if (small)
        {
            foreach (var viewer in mPlayers.Where(audience))
            {
                SendTo(viewer, PacketId.EntityLookRelMove, w =>
                {
                    w.WriteInt(entity.Id);
                    w.WriteSByte(dx);
                    w.WriteSByte(dy);
                    w.WriteSByte(dz);
                    w.WriteAngle(yawDegrees);
                    w.WriteAngle(0f);
                });
            }

            // The delta that was SENT, not the one that happened - otherwise the 1/32 truncation
            // accumulates and the client's copy drifts away from this one.
            entity.LastSentPosition += new Vector3(dx, dy, dz) / 32f;
        }
        else
        {
            foreach (var viewer in mPlayers.Where(audience))
            {
                SendTo(viewer, PacketId.EntityTeleport, w =>
                {
                    w.WriteInt(entity.Id);
                    w.WriteFixedPos(entity.Position);
                    w.WriteAngle(yawDegrees);
                    w.WriteAngle(0f);
                });
            }

            entity.LastSentPosition = entity.Position;
        }

        entity.TicksSinceUpdate = 0;
    }

    /// Everything the server replicates except players and dropped items, which have their own
    /// spawn packets and their own tracking.
    private IEnumerable<Entity> TrackableMobs() =>
        mWorld.Entities.Where(e => e.IsAlive && e is not Player && e is not DroppedItemEntity &&
                                   (MobTypeIdOf(e) != 0 || ObjectTypeIdOf(e) != 0 || e is PaintingEntity));

    /// Beta's "object" ids, for things that move but aren't alive. 0 means "not one of these".
    internal static byte ObjectTypeIdOf(Entity entity) => entity switch
    {
        ArrowEntity => 60,
        TntEntity => 50,
        FallingBlockEntity f => f.Block == BlockType.Gravel ? (byte)71 : (byte)70,
        _ => 0,
    };

    private bool IsMobId(int id) =>
        mPlayers.All(p => p.EntityId != id);

    /// Beta's mob type ids, kept where they line up. The client maps them straight back to a class,
    /// so the only requirement is that both ends agree.
    internal static byte MobTypeIdOf(Entity mob) => mob switch
    {
        Stalker => 50,                                 // creeper's slot
        Skeleton => 51,
        Spider => 52,
        Zombie => 54,
        Pig => 90,
        Sheep => 91,
        _ => 0,                                        // not replicated
    };
}

// Client half of world sync: block batches, the clock, effects, health and mobs. | Stage 11

using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Core;

public partial class Game
{
    // --- blocks ---------------------------------------------------------------------------------

    /// A whole chunk's worth of changes in one packet - what a flowing river or a collapsing sand
    /// column looks like. Each entry goes through the same deferral as a single BlockChange, because
    /// the chunk it belongs to may not have integrated yet.
    private void OnMultiBlockChange(NetStream r)
    {
        int chunkX = r.ReadInt();
        int chunkZ = r.ReadInt();
        int count = r.ReadShort();

        var packed = new short[count];
        for (int i = 0; i < count; i++) packed[i] = r.ReadShort();

        var types = new byte[count];
        for (int i = 0; i < count; i++) types[i] = r.ReadByte();

        var metadata = new byte[count];
        for (int i = 0; i < count; i++) metadata[i] = r.ReadByte();

        for (int i = 0; i < count; i++)
        {
            int lx = (packed[i] >> 12) & 0xF;
            int lz = (packed[i] >> 8) & 0xF;
            int y = packed[i] & 0xFF;

            ReceiveBlockChange(chunkX * Chunk.WIDTH + lx, y, chunkZ * Chunk.DEPTH + lz,
                               (BlockType)types[i], metadata[i]);
        }
    }

    // --- the clock ------------------------------------------------------------------------------

    /// A periodic drift correction, not the only thing moving the clock - UpdateGameLogic advances
    /// mTimeOfDay locally every frame too, so the sun stays smooth between these.
    private void OnTimeUpdate(NetStream r) => SetTimeOfDay(r.ReadLong() % 24000 / 24000f);

    // --- effects --------------------------------------------------------------------------------

    private const float EFFECT_MAX_DISTANCE = 16f;

    /// Someone else mined something. The packet carries no volume, so distance is applied here.
    private void OnSoundEffect(NetStream r)
    {
        var effect = (EffectId)r.ReadInt();
        int x = r.ReadInt();
        int y = r.ReadByte();
        int z = r.ReadInt();
        int data = r.ReadInt();

        var pos = new Vector3i(x, y, z);
        float volume = VolumeByDistance(pos);

        if (volume <= 0f)
            return;

        switch (effect)
        {
            case EffectId.BlockBreak:
                var block = (BlockType)data;
                ParticleSystem.SpawnBlockBreakParticles(pos.ToVector3(), block);
                AudioManager.PlayBlockBreakSound(BlockRegistry.GetBlockBreakMaterial(block), volume);
                break;
        }
    }

    private float VolumeByDistance(Vector3i pos)
    {
        if (mPlayer == null)
            return 0f;

        float distance = Vector3.Distance(pos.ToVector3(), mPlayer.Position);
        return Math.Clamp(1f - distance / EFFECT_MAX_DISTANCE, 0f, 1f);
    }

    // --- health ---------------------------------------------------------------------------------

    private void OnUpdateHealth(NetStream r)
    {
        short health = r.ReadShort();

        if (mPlayer == null)
            return;

        if (health < mPlayer.Health)
            AudioManager.PlayPlayerHurtSound();

        mPlayer.Health = health;

        // Health 0 IS the death signal - beta had no separate death packet. Any in-world state can
        // die, not just Playing: dying with a chest open used to leave you in the chest UI on zero
        // health with no death screen.
        if (health <= 0 && CurrentState is GameState.Playing or GameState.Paused or GameState.Inventory
                or GameState.Crafting or GameState.Furnace or GameState.Chest or GameState.DoubleChest)
        {
            Windows.Close();                            // the server closed its side in KillPlayer
            CurrentState = GameState.Died;
            SetCursorGrabbed(false);
        }
    }

    private void OnRespawn(NetStream r)
    {
        r.ReadByte();                                   // dimension, ignored

        if (mPlayer != null)
            mPlayer.Health = Player.PLAYER_MAX_HEALTH;

        CurrentState = GameState.Playing;
        SetCursorGrabbed(true);
    }

    /// Beta's "drop what I'm holding": PlayerDigging status 4, position ignored.
    private void SendDropHeld() =>
        mNetwork?.Send(PacketId.PlayerDigging, w =>
        {
            w.WriteByte(4);
            w.WriteInt(0);
            w.WriteByte(0);
            w.WriteInt(0);
            w.WriteByte(0);
        });

    /// Using a non-block item. Beta reused the placement packet for this: the block being looked at
    /// plus a face, or 255s for "used it in the air" (a bow, a bucket pointed at nothing).
    private void SendUseItem()
    {
        var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
        bool hasBlock = hit.Type == RaycastHitType.Block;

        var clicked = hasBlock ? hit.BlockPos : default;
        byte face = hasBlock && hit.PlacePos is { } place ? FaceBetween(clicked, place) : (byte)255;

        mNetwork?.Send(PacketId.PlayerBlockPlacement, w =>
        {
            w.WriteInt(clicked.X);
            w.WriteByte(hasBlock ? (byte)clicked.Y : (byte)255);
            w.WriteInt(clicked.Z);
            w.WriteByte(face);
            w.WriteItem(null);                          // the server reads its own held slot
        });
    }

    /// Beta's "I hit that" packet. The server decides whether it landed.
    private void SendAttack(int targetId) =>
        mNetwork?.Send(PacketId.UseEntity, w =>
        {
            w.WriteInt(mNetwork.LocalEntityId);
            w.WriteInt(targetId);
            w.WriteByte(1);                             // 1 = attack, 0 = interact
        });

    /// The death screen's Respawn button. The server decides where we come back.
    internal void RequestRespawn() => mNetwork?.Send(PacketId.Respawn, w => w.WriteByte(0));

    /// Knockback. Only our own velocity matters - everyone else's position comes from the server
    /// anyway, so applying it to a proxy would just fight the next movement packet.
    private void OnEntityVelocity(NetStream r)
    {
        int entityId = r.ReadInt();
        float vx = r.ReadShort() / 800f;
        float vy = r.ReadShort() / 800f;
        float vz = r.ReadShort() / 800f;

        if (mPlayer != null && entityId == mNetwork?.LocalEntityId)
            mPlayer.Velocity += new Vector3(vx, vy, vz);
    }

    // --- mobs -----------------------------------------------------------------------------------

    /// Server-owned entities that aren't players - mobs and dropped items - by entity id. They live
    /// in World.Entities too, so the normal render path draws them; this is the lookup the movement
    /// packets need.
    private readonly Dictionary<int, Entity> mRemoteMobs = new();

    /// An item lying on the ground. Everything about it is the server's: where it is, and who gets it.
    private void OnPickupSpawn(NetStream r)
    {
        int entityId = r.ReadInt();
        short itemId = r.ReadShort();
        byte count = r.ReadByte();
        r.ReadShort();                                  // durability
        var position = r.ReadFixedPos();
        r.ReadByte(); r.ReadByte(); r.ReadByte();       // rotation, pitch, roll - unused

        if (mRemoteMobs.ContainsKey(entityId) || DecodeHeld(itemId) is not { } stack)
            return;

        var drop = new DroppedItemEntity(position, stack.WithCount(count));
        drop.AssignNetworkId(entityId);
        drop.IsRemoteProxy = true;
        drop.NetTargetPosition = position;

        mRemoteMobs[entityId] = drop;
        mWorld.AddEntity(drop);
    }

    /// Someone picked something up. DestroyEntity follows, so this is only the sound.
    private void OnCollectItem(NetStream r)
    {
        r.ReadInt();                                    // the drop
        int collectorId = r.ReadInt();

        if (collectorId == mNetwork?.LocalEntityId)
            AudioManager.PlayPickupSound();
    }

    private void OnMobSpawn(NetStream r)
    {
        int entityId = r.ReadInt();
        byte type = r.ReadByte();
        var position = r.ReadFixedPos();
        float yaw = r.ReadAngle();
        r.ReadByte();                                   // pitch, unused - mobs don't pitch
        r.ReadMetadata();

        if (mRemoteMobs.ContainsKey(entityId))
            return;

        if (CreateMob(type, position) is not { } mob)
            return;

        mob.Yaw = float.DegreesToRadians(yaw);          // the wire is degrees; mob rotation is radians

        // Render-only: its AI would path independently and its physics would fight the server.
        RegisterProxy(entityId, mob, position);
    }

    /// Beta's "object" spawn: things that move but aren't alive. Arrows and primed TNT are the two
    /// we make, and both are render-only here - the server owns the flight and the explosion.
    private void OnAddObject(NetStream r)
    {
        int entityId = r.ReadInt();
        byte type = r.ReadByte();
        var position = r.ReadFixedPos();
        int thrower = r.ReadInt();

        if (thrower > 0)
        {
            r.ReadShort(); r.ReadShort(); r.ReadShort();   // velocity tail, only sent with a thrower
        }

        if (mRemoteMobs.ContainsKey(entityId))
            return;

        Entity? obj = type switch
        {
            60 => new ArrowEntity(mPlayer, position, Vector3.UnitX),
            50 => new TntEntity(position, BlockRegistry.Get(BlockType.TNT)),
            // Beta gives falling sand and gravel their own object ids rather than one id plus a
            // block field, so the type byte is all that's needed to rebuild them.
            70 => new FallingBlockEntity(position, BlockType.Sand),
            71 => new FallingBlockEntity(position, BlockType.Gravel),
            _ => null,
        };

        if (obj == null)
            return;

        obj.Position = position;
        RegisterProxy(entityId, obj, position);
    }

    /// Paintings hang on a wall and never move. They live in the server's world, so on a client they
    /// only exist because of this packet.
    private void OnEntityPainting(NetStream r)
    {
        int entityId = r.ReadInt();
        string art = r.ReadString();
        var anchor = new Vector3i(r.ReadInt(), r.ReadInt(), r.ReadInt());
        byte facing = (byte)r.ReadInt();

        if (mRemoteMobs.ContainsKey(entityId))
            return;

        var painting = new PaintingEntity(anchor, facing, PaintingRegistry.GetByName(art));
        RegisterProxy(entityId, painting, painting.Position);
    }

    /// Adds a server-owned entity to the world as a render-only proxy.
    private void RegisterProxy(int entityId, Entity entity, Vector3 position)
    {
        entity.AssignNetworkId(entityId);
        entity.IsRemoteProxy = true;
        entity.NetTargetPosition = position;

        mRemoteMobs[entityId] = entity;
        mWorld.AddEntity(entity);
    }

    /// The inverse of the server's MobTypeIdOf. Unknown ids are ignored rather than guessed - a mob
    /// drawn as the wrong thing is worse than one that isn't drawn.
    private static Entity? CreateMob(byte type, Vector3 position) => type switch
    {
        50 => new Stalker(position),
        51 => new Skeleton(position),
        52 => new Spider(position),
        54 => new Zombie(position),
        90 => new Pig(position),
        91 => new Sheep(position),
        _ => null,
    };

    private void RemoveRemoteMob(int entityId)
    {
        if (!mRemoteMobs.Remove(entityId, out var mob))
            return;

        mWorld.RemoveEntity(mob);
    }
}

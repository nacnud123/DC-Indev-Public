// Health, damage, death and respawn. The client shows what it is told. | Stage 11

using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private const int HURT_COOLDOWN_TICKS = 10;        // beta's half-second of invulnerability
    private const float FALL_SAFE_BLOCKS = 3f;
    private const float REACH = 6f;

    // Player.BREATH_MAX and the client's lava/fire burn durations, in ticks.
    private const int BREATH_MAX_TICKS = (int)(Player.BREATH_MAX * TickSystem.TPS);
    private const int LAVA_FIRE_TICKS = 15 * TickSystem.TPS;
    private const int STANDING_FIRE_TICKS = 8 * TickSystem.TPS;

    /// Every damage source funnels through here, so there is exactly one place that can kill
    /// someone - and one place that has to remember to tell them about it.
    internal void DamagePlayer(ServerPlayer player, int amount, string deathMessage)
    {
        if (player.IsDead || amount <= 0 || player.HurtCooldownTicks > 0)
            return;

        // The client's Player.TakeDamage reads GameContext.Current.PlayerInventory, which on a
        // server is nobody's inventory in particular - so the armour maths is done here instead.
        int armour = player.Inventory.GetArmorValue();
        int actual = Math.Max(1, amount * (25 - armour) / 25);

        player.Inventory.DamageArmor(amount);
        player.Entity.Health = Math.Max(0, player.Entity.Health - actual);
        player.HurtCooldownTicks = HURT_COOLDOWN_TICKS;

        SendTo(player, PacketId.UpdateHealth, w => w.WriteShort((short)player.Entity.Health));
        SendInventory(player);                          // armour durability changed

        // Everyone nearby sees the hurt animation.
        BroadcastExcept(player, PacketId.Animation, w =>
        {
            w.WriteInt(player.EntityId);
            w.WriteByte(2);
        });

        if (player.Entity.Health > 0)
            return;

        KillPlayer(player, deathMessage);
    }

    private void KillPlayer(ServerPlayer player, string deathMessage)
    {
        player.IsDead = true;
        mLog.Log(LogLevel.Info, $"{player.Name} {deathMessage}");
        Broadcast(PacketId.ChatMessage, w => w.WriteString($"{player.Name} {deathMessage}"));

        // Beta dropped your whole inventory where you died.
        for (int i = 0; i < PlayerInventory.TOTAL_SLOTS; i++)
        {
            if (player.Inventory.GetSlot(i) is not { } stack)
                continue;

            SpawnDrop(player.Position + new Vector3(0f, 1f, 0f), stack, null);
            player.Inventory.SetSlot(i, null);
        }

        if (player.Cursor is { } held)
        {
            SpawnDrop(player.Position + new Vector3(0f, 1f, 0f), held, null);
            player.Cursor = null;
        }

        CloseAllWindows(player);
        SendInventory(player);

        // Health 0 is what tells the client to show its death screen - there is no death packet.
        SendTo(player, PacketId.UpdateHealth, w => w.WriteShort(0));
    }

    /// The client pressed Respawn on the death screen.
    private void HandleRespawn(ServerPlayer player, NetStream r)
    {
        r.ReadByte();                                   // dimension, ignored

        if (!player.IsDead)
            return;                                     // ignore spurious respawns

        player.IsDead = false;
        player.Entity.Health = Player.PLAYER_MAX_HEALTH;
        player.HurtCooldownTicks = HURT_COOLDOWN_TICKS;
        player.FallDistance = 0f;
        player.ResetEnvironment(BREATH_MAX_TICKS);

        var spawn = FindSafeSpawn(mSpawn);
        player.Position = spawn;
        player.Entity.NetTargetPosition = spawn;
        player.LastY = spawn.Y;

        // Same handshake TeleportPlayer arms: a respawn is a teleport as far as the movement
        // checks are concerned.
        player.LastGoodPosition = spawn;
        player.HasMoved = false;

        SendTo(player, PacketId.Respawn, w => w.WriteByte(0));
        SendTo(player, PacketId.UpdateHealth, w => w.WriteShort((short)player.Entity.Health));
        SendTo(player, PacketId.PlayerPositionLook, w => WritePositionLook(w, player));

        // They have teleported; every chunk they held is wrong now, and so is everyone else's idea
        // of where they are.
        player.SentChunks.Clear();
        player.TrackedEntities.Clear();

        BroadcastExcept(player, PacketId.EntityTeleport, w =>
        {
            w.WriteInt(player.EntityId);
            w.WriteFixedPos(player.Position);
            w.WriteAngle(player.Yaw);
            w.WriteAngle(player.Pitch);
        });
    }

    /// Server-side fall damage. The client sends positions; the server watches for a fall ending.
    /// The player entity is a proxy here (physics never runs), so there is no mFallDistance to read.
    private void CheckFallDamage(ServerPlayer player)
    {
        if (player.IsDead)
        {
            player.LastY = player.Position.Y;
            return;
        }

        float dy = player.Position.Y - player.LastY;
        player.LastY = player.Position.Y;

        // Landing in a fluid hurts nobody. Singleplayer gets this for free - Player.TickFluid zeroes
        // mFallDistance every tick it swims - but the server watches position deltas rather than
        // running physics, so without this you took the full fall for diving into a lake.
        if (IsInFluid(player))
        {
            player.FallDistance = 0f;
            return;
        }

        if (dy < -0.08f)
        {
            player.FallDistance += -dy;                 // still falling
            return;
        }

        if (dy > 0.08f || player.FallDistance <= 0f)
        {
            player.FallDistance = 0f;                   // climbing, or never left the ground
            return;
        }

        float distance = player.FallDistance;
        player.FallDistance = 0f;

        int damage = (int)MathF.Ceiling(distance - FALL_SAFE_BLOCKS);
        if (damage > 0)
            DamagePlayer(player, damage, "fell from a high place");
    }

    private bool IsInFluid(ServerPlayer player) =>
        BlockFluid.ContainsPoint(mWorld, BlockType.Water, player.Position) ||
        BlockFluid.ContainsPoint(mWorld, BlockType.Lava, player.Position);

    /// <summary>
    /// Drowning, lava and burning. The client runs all of this in Player.Tick, but its TakeDamage
    /// returns immediately when ServerOwnsHealth is set - so in multiplayer none of it applied to
    /// anyone, and you could sit on the bottom of the ocean or swim laps in lava indefinitely.
    /// Timings mirror the client's: 15s of breath, then 2 damage a second; 2 damage every half
    /// second in lava; 1 a second while burning.
    /// </summary>
    private void TickEnvironmentalDamage(ServerPlayer player)
    {
        if (player.IsDead)
            return;

        bool inWater = BlockFluid.ContainsPoint(mWorld, BlockType.Water, player.Position);
        bool inLava = BlockFluid.ContainsPoint(mWorld, BlockType.Lava, player.Position);

        // The eye, not the feet: standing chest-deep is not drowning.
        var eye = player.Position with { Y = player.Position.Y + player.Entity.EyeHeight };
        bool submerged = BlockFluid.ContainsPoint(mWorld, BlockType.Water, eye);

        if (submerged)
        {
            if (player.BreathTicks > 0)
            {
                player.BreathTicks--;
            }
            else if (--player.DrownCooldownTicks <= 0)
            {
                player.DrownCooldownTicks = TickSystem.TPS;
                DamagePlayer(player, 2, "drowned");
            }
        }
        else
        {
            // Twice the depletion rate, as on the client - a quick gasp at the surface is enough.
            player.BreathTicks = Math.Min(BREATH_MAX_TICKS, player.BreathTicks + 2);
            player.DrownCooldownTicks = TickSystem.TPS;
        }

        if (inLava)
        {
            if (--player.LavaCooldownTicks <= 0)
            {
                player.LavaCooldownTicks = TickSystem.TPS / 2;
                DamagePlayer(player, 2, "tried to swim in lava");
            }

            // Refreshed every tick rather than set on entry, so climbing out still leaves you alight.
            player.FireTicks = LAVA_FIRE_TICKS;
        }
        else
        {
            player.LavaCooldownTicks = 0;
        }

        var foot = new Vector3i((int)MathF.Floor(player.Position.X),
                                (int)MathF.Floor(player.Position.Y),
                                (int)MathF.Floor(player.Position.Z));

        if (mWorld.GetBlock(foot.X, foot.Y, foot.Z) == BlockType.Fire)
            player.FireTicks = Math.Max(player.FireTicks, STANDING_FIRE_TICKS);

        if (inWater)
            player.FireTicks = 0;

        if (player.FireTicks > 0)
        {
            player.FireTicks--;
            if (--player.BurnCooldownTicks <= 0)
            {
                player.BurnCooldownTicks = TickSystem.TPS;
                DamagePlayer(player, 1, "burned to death");
            }
        }
        else
        {
            player.BurnCooldownTicks = 0;
        }
    }

    /// PvP. Beta let the client claim "I hit that entity"; the server checks reach and applies the
    /// damage itself, because a client that decides its own hits decides everyone's health.
    private void HandleUseEntity(ServerPlayer attacker, NetStream r)
    {
        r.ReadInt();                                    // attacker id as claimed - we know who sent it
        int targetId = r.ReadInt();
        bool leftClick = r.ReadByte() != 0;

        if (!leftClick || attacker.IsDead)
            return;                                     // right-click is "interact", not "attack"

        var held = attacker.Inventory.GetSlot(PlayerInventory.HOTBAR_START +
                       Math.Clamp((int)attacker.HeldSlot, 0, PlayerInventory.HOTBAR_SLOTS - 1));

        int damage = held is { IsBlock: false } item ? ItemRegistry.Get(item.Item).AttackDamage : 1;

        var targetPlayer = mPlayers.FirstOrDefault(p => p.EntityId == targetId);
        if (targetPlayer != null)
        {
            if (!mProps.Pvp || targetPlayer == attacker || targetPlayer.IsDead)
                return;

            if ((targetPlayer.Position - attacker.Position).Length() > REACH)
                return;

            DamagePlayer(targetPlayer, damage, $"was slain by {attacker.Name}");
            Knockback(targetPlayer.EntityId, targetPlayer.Position, attacker.Position);
            return;
        }

        // Mobs: same reach check, and the server's own entity takes the hit.
        var mob = mWorld.Entities.FirstOrDefault(e => e.Id == targetId && e is not Player);
        if (mob == null || !mob.IsAlive || (mob.Position - attacker.Position).Length() > REACH)
            return;

        mob.TakeDamage(damage);
        Knockback(mob.Id, mob.Position, attacker.Position);

        if (!mob.IsAlive)
            Broadcast(PacketId.DestroyEntity, w => w.WriteInt(mob.Id));
    }

    /// Sent to everyone: the victim moves themselves, and everyone else sees them move.
    private void Knockback(int entityId, Vector3 target, Vector3 source)
    {
        var away = target - source;
        away.Y = 0f;

        if (away.LengthSquared() < 0.0001f)
            away = new Vector3(0f, 0f, 1f);

        var push = Vector3.Normalize(away) * 8f;

        Broadcast(PacketId.EntityVelocity, w =>
        {
            w.WriteInt(entityId);
            w.WriteShort((short)(push.X * 800));         // beta's velocity unit is 1/8000 blocks/tick
            w.WriteShort(3000);
            w.WriteShort((short)(push.Z * 800));
        });
    }
}

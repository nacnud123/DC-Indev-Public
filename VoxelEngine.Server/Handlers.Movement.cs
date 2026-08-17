using VoxelEngine.Net;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    /// b1.7.3's world border. Past this the chunk streamer would happily start generating terrain
    /// at coordinates no one can reach, which is the cheapest way there is to melt a server.
    private const double MAX_WORLD_COORD = 32_000_000;

    /// Vanilla's "moved too quickly!" threshold, in blocks squared per movement packet. 10 blocks in
    /// one packet is far beyond sprinting or terminal velocity, so this only catches teleport hacks.
    private const float MAX_MOVE_SQUARED = 100f;

    /// How close the client's echoed position must be to a server teleport before we start trusting
    /// its movement again.
    private const float TELEPORT_ACK_TOLERANCE = 0.5f;

    private void HandleMovement(ServerPlayer player, PacketId id, NetStream r)
    {
        if (id is PacketId.PlayerPosition or PacketId.PlayerPositionLook)
        {
            double x = r.ReadDouble();
            r.ReadDouble();
            double y = r.ReadDouble();
            double z = r.ReadDouble();

            // Dropping the rest of the body is free: it's a private MemoryStream per packet, not the
            // socket, so an unread field desynchronises nothing.
            if (!AcceptPosition(player, x, y, z))
                return;
        }

        if (id is PacketId.PlayerLook or PacketId.PlayerPositionLook)
        {
            player.Yaw = r.ReadFloat();
            player.Pitch = r.ReadFloat();
            player.Entity.Yaw = player.Yaw;                 // what mob AI aims at
        }

        r.ReadBool();
    }

    /// <summary>
    /// b1.7.3's handleFlying checks, which this had none of: the client's claimed position was
    /// simply believed. That let a modified client teleport at will - and since the dig and place
    /// handlers measure reach from this position, teleporting is also how you mine across the map -
    /// while an absurd coordinate aimed the chunk streamer at terrain nobody could reach.
    /// </summary>
    /// <returns>true if the position was accepted and applied.</returns>
    private bool AcceptPosition(ServerPlayer player, double x, double y, double z)
    {
        // Vanilla checked only the magnitude, which is why NaN got through it: every comparison
        // against NaN is false, so it passed the bounds check and then poisoned the player's
        // position, the chunk coords derived from it, and finally the saved player file.
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
            Math.Abs(x) > MAX_WORLD_COORD || Math.Abs(z) > MAX_WORLD_COORD)
        {
            player.Connection.Kick("Illegal position");
            return false;
        }

        var position = new Vector3((float)x, (float)y, (float)z);

        // A teleport we sent is still in flight: the packets arriving now describe where they were
        // before it, so applying them would drag them back and trip the speed check on the way.
        if (!player.HasMoved)
        {
            if (Vector3.DistanceSquared(position, player.LastGoodPosition) <
                TELEPORT_ACK_TOLERANCE * TELEPORT_ACK_TOLERANCE)
            {
                player.HasMoved = true;
            }

            return false;
        }

        if (Vector3.DistanceSquared(position, player.LastGoodPosition) > MAX_MOVE_SQUARED)
        {
            mLog.Log(LogLevel.Warning, $"{player.Name} moved too quickly!");
            TeleportPlayer(player, player.LastGoodPosition);   // rubber-band, as vanilla does
            return false;
        }

        player.Position = position;
        player.LastGoodPosition = position;

        // The entity is a proxy here (see PlayerData): its tick eases toward this, so leaving
        // the target behind would drag the player backwards every tick.
        player.Entity.NetTargetPosition = position;
        return true;
    }

    /// Everyone but the player themself; the maths lives in SendMovement, which mobs and dropped
    /// items share.
    private void BroadcastMovement(ServerPlayer player) =>
        SendMovement(player.Entity, player.Yaw, viewer => viewer != player);
}

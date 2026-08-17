using VoxelEngine.Net;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private const int PROTOCOL_VERSION = 14;

    /// A socket that connects and then says nothing holds a read thread and a pending slot. Beta had
    /// the same 30-second grace period before giving up on a handshake.
    private const long LOGIN_TIMEOUT_MS = 30_000;

    private void AcceptPendingLogins()
    {
        int count = mPending.Count; // bounded: don't loop over re-queued entries forever

        for (int i = 0; i < count; i++)
        {
            if (!mPending.TryDequeue(out var conn)) return;
            if (!conn.Connected) continue;
            if (!conn.Inbox.TryDequeue(out var packet))
            {
                if (Environment.TickCount64 - conn.AcceptedAt > LOGIN_TIMEOUT_MS)
                {
                    conn.Kick("Took too long to log in");
                    continue;
                }

                mPending.Enqueue(conn);
                continue;
            }

            var r = packet.OpenBody();

            if (packet.Id == PacketId.Handshake)
            {
                string name = r.ReadString();
                conn.PendingName = name;
                // "-" means "offline mode, no authentication". That IS the beta answer - there is
                // no account check, and anyone can claim any name.
                conn.Send(PacketId.Handshake, w => w.WriteString("-"));
                mPending.Enqueue(conn); // wait for the LoginRequest that follows
                continue;
            }

            if (packet.Id != PacketId.LoginRequest)
            {
                conn.Kick("Protocol error");
                continue;
            }

            int protocol = r.ReadInt();
            string username = r.ReadString();
            r.ReadLong();
            r.ReadByte(); // client sends seed+dimension here; both ignored

            if (protocol != PROTOCOL_VERSION)
            {
                conn.Kick("Outdated client!");
                continue;
            }

            if (mPlayers.Count >= mProps.MaxPlayers)
            {
                conn.Kick("The server is full!");
                continue;
            }

            if (mProps.IsBanned(username))
            {
                conn.Kick("You are banned from this server!");
                continue;
            }

            if (mProps.WhitelistEnabled && !mProps.IsWhitelisted(username))
            {
                conn.Kick("You are not white-listed!");
                continue;
            }

            if (mPlayers.Any(p => p.Name == username))
            {
                conn.Kick("That name is already taken");
                continue;
            }

            var player = CreateOrLoadPlayer(username, conn);

            // Seeds the speed check with where we just put them. HasMoved stays true: a fresh
            // connection has no in-flight movement to wait out, and a missed ack would freeze them.
            player.LastGoodPosition = player.Position;
            player.HasMoved = true;

            // Breath is not saved, so a fresh session starts with full lungs rather than drowning
            // on the first tick because the counter was zero.
            player.ResetEnvironment(BREATH_MAX_TICKS);

            mPlayers.Add(player);
            mWorld.AddEntity(player.Entity);
            mWorld.Players.Add(player.Entity);

            // Server's half of the login handshake: your entity id, the seed, the dimension.
            SendTo(player, PacketId.LoginRequest, w =>
            {
                w.WriteInt(player.EntityId);
                w.WriteString("");
                w.WriteLong(mWorld.Seed);
                w.WriteByte(0); // dimension 0 = overworld
            });

            SendTo(player, PacketId.SpawnPosition, w =>
            {
                w.WriteInt(mSpawn.X);
                w.WriteInt(mSpawn.Y);
                w.WriteInt(mSpawn.Z);
            });

            SendTo(player, PacketId.PlayerPositionLook, w => WritePositionLook(w, player));

            // Window 0 up front: without it the client's inventory mirror is empty until the first
            // pickup, so a returning player sees none of their saved items.
            SendInventory(player);
            BroadcastHeldItem(player);

            mLog.Log(LogLevel.Info, $"{username} [{RemoteAddress(conn)}] logged in with entity id {player.EntityId}");
            Broadcast(PacketId.ChatMessage, w => w.WriteString($"§e{username} joined the game"));

            // Everyone else needs to see them; they need to see everyone else.
            BroadcastExcept(player, PacketId.NamedEntitySpawn, w => WriteNamedSpawn(w, player));
            foreach (var other in mPlayers.Where(p => p != player))
            {
                SendTo(player, PacketId.NamedEntitySpawn, w => WriteNamedSpawn(w, other));
                SendHeldItem(player, other);
            }
        }
    }

    private void DropDeadConnections()
    {
        for (int i = mPlayers.Count - 1; i >= 0; i--)
        {
            if (mPlayers[i].Connection.Connected) continue;

            var gone = mPlayers[i];

            // Their windows would otherwise sit in the dictionary forever, holding a cursor stack
            // that never comes back and a session that broadcasts to a dead connection.
            CloseAllWindows(gone);
            SavePlayer(gone);
            mWorld.RemoveEntity(gone.Entity);
            mWorld.Players.Remove(gone.Entity);
            mPlayers.RemoveAt(i);

            mLog.Log(LogLevel.Info, $"{gone.Name} lost connection: {gone.Connection.KickReason ?? "disconnect"}");
            Broadcast(PacketId.DestroyEntity, w => w.WriteInt(gone.EntityId));
            Broadcast(PacketId.ChatMessage, w => w.WriteString($"§e{gone.Name} left the game"));
        }
    }
}
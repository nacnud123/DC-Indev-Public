// The small shared functions every handler file reaches for. | Stage 9

using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private Vector3i mSpawn;
    private long mWorldTime;
    private long mTickCount;

    // --- geometry ---

    /// Beta's block face numbering, as sent in PlayerBlockPlacement.
    internal static Vector3i FaceOffset(byte face) => face switch
    {
        0 => new Vector3i(0, -1, 0),   // bottom
        1 => new Vector3i(0,  1, 0),   // top
        2 => new Vector3i(0, 0, -1),   // north
        3 => new Vector3i(0, 0,  1),   // south
        4 => new Vector3i(-1, 0, 0),   // west
        _ => new Vector3i( 1, 0, 0),   // east
    };

    internal static Vector3i ToBlockPos(Vector3 p) =>
        new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    internal static Vector3 ForwardFromYaw(float yawDegrees)
    {
        float rad = float.DegreesToRadians(yawDegrees);
        return Vector3.Normalize(new Vector3(MathF.Cos(rad), 0f, MathF.Sin(rad)));
    }

    // --- mining ---

    /// How long this block SHOULD take, so a client claiming an instant break can be ignored.
    /// Mirrors the client's mining-speed maths in Player.Interaction.cs - change one, change both,
    /// or legitimate players get rejected.
    internal static int RequiredDigTicks(BlockType block, ItemStack? heldItem)
    {
        float hardness = BlockRegistry.GetHardness(block);
        if (hardness <= 0f)
            return 0;                                        // instant-break (tall grass, flowers)

        float speed = 1f;
        if (heldItem is { IsBlock: false } item)
        {
            var def = ItemRegistry.Get(item.Item);
            if (def.IsTool && def.ToolType == BlockRegistry.Get(block).PreferredTool)
                speed = def.MiningSpeed;
        }

        // Generous: dig checks exist to stop absurd claims, not to police a 50ms timing difference.
        return Math.Max(1, (int)(hardness * 20f / speed * 0.7f));
    }

    // --- teleport and spawning ---

    internal void TeleportPlayer(ServerPlayer player, Vector3 destination)
    {
        player.Position = destination;

        // Arms the teleport handshake in AcceptPosition. Without this every /tp and respawn would
        // read as a 10-block jump and be rubber-banded straight back by the speed check.
        player.LastGoodPosition = destination;
        player.HasMoved = false;

        SendTo(player, PacketId.PlayerPositionLook, w => WritePositionLook(w, player));

        // Everything they were holding is wrong now - forget it so the streamer resends around
        // where they actually are.
        player.SentChunks.Clear();
        player.TrackedEntities.Clear();

        BroadcastExcept(player, PacketId.EntityTeleport, w =>
        {
            w.WriteInt(player.EntityId);
            w.WriteFixedPos(destination);
            w.WriteAngle(player.Yaw);
            w.WriteAngle(player.Pitch);
        });
    }

    /// Scans down a column for two blocks of air above something solid, so /tp and respawn never
    /// drop someone inside terrain.
    internal Vector3 FindSafeSpawn(Vector3i near)
    {
        for (int y = Chunk.HEIGHT - 2; y > 1; y--)
        {
            if (!BlockRegistry.IsSolid(mWorld.GetBlock(near.X, y - 1, near.Z))) continue;
            if (mWorld.GetBlock(near.X, y, near.Z) != BlockType.Air) continue;
            if (mWorld.GetBlock(near.X, y + 1, near.Z) != BlockType.Air) continue;

            return new Vector3(near.X + 0.5f, y, near.Z + 0.5f);
        }

        return new Vector3(near.X + 0.5f, Chunk.HEIGHT / 2, near.Z + 0.5f);
    }

    // --- packet field writers shared across handlers ---

    /// `stance` sits between x and y going out and comes back in that same odd slot - it's eye height.
    internal static void WritePositionLook(NetStream w, ServerPlayer p)
    {
        w.WriteDouble(p.Position.X);
        w.WriteDouble(p.Position.Y + 1.62);
        w.WriteDouble(p.Position.Y);
        w.WriteDouble(p.Position.Z);
        w.WriteFloat(p.Yaw);
        w.WriteFloat(p.Pitch);
        w.WriteBool(true);                                   // onGround
    }

    internal static void WriteNamedSpawn(NetStream w, ServerPlayer p)
    {
        w.WriteInt(p.EntityId);
        w.WriteString(p.Name);
        w.WriteFixedPos(p.Position);
        w.WriteAngle(p.Yaw);
        w.WriteAngle(p.Pitch);
        w.WriteShort(0);                                     // held item, updated by EntityEquipment
    }

    internal static string RemoteAddress(ServerConnection conn) => conn.RemoteEndPoint ?? "?";

    // --- lifecycle ---

    private World LoadOrCreateWorld()
    {
        Serialization.WorldName = mProps.LevelName;

        // LevelSeed is a long (real Minecraft seeds are) and World.Seed is an int, so this narrows.
        // Widening World.Seed to long is the proper fix - it touches world_info.xml serialization.
        int seed = mProps.LevelSeed != 0 ? (int)mProps.LevelSeed : new Random().Next();
        var world = new World(seed);

        // Force the spawn chunks resident before anyone can join, or the first player logs into a
        // void and falls. Beta printed "Preparing spawn area" while doing exactly this.
        mLog.Log(LogLevel.Info, "Preparing spawn area");
        world.PrimeAround(Vector3.Zero, radiusChunks: 3);

        mWorld = world;                                      // FindSafeSpawn reads it
        mSpawn = ToBlockPos(FindSafeSpawn(new Vector3i(0, 0, 0)));

        mLog.Log(LogLevel.Info,
            $"Preparing level \"{mProps.LevelName}\" (spawn {mSpawn.X}, {mSpawn.Y}, {mSpawn.Z})");
        return world;
    }

    /// Called from Program.cs on the MAIN thread, so it may only touch the concurrent queue.
    /// RunConsoleCommand then executes it on the tick thread.
    public void EnqueueConsoleCommand(string line) => mConsoleCommands.Enqueue(line);

    private void RunConsoleCommand(string line)
    {
        mLog.Log(LogLevel.Command, "> " + line);
        RunCommand(sender: null, line.TrimStart('/'));       // null sender = console, always op
    }

    internal void Shutdown()
    {
        mLog.Log(LogLevel.Info, "Stopping the server");

        foreach (var p in mPlayers.ToList())
            p.Connection.Kick("Server closed");

        SaveEverything(blocking: true);      // the process exits right after; a fire-and-forget save would be killed mid-write
        Running = false;
        mListener.Stop();                    // unblocks the accept loop, which would otherwise sit on AcceptTcpClientAsync
    }
}

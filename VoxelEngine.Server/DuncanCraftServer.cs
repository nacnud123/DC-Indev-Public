using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VoxelEngine.Core;
using VoxelEngine.Net;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    public bool Running { get; private set; } = true;

    private readonly ServerProperties mProps;
    private readonly TickSystem mTicks = new();
    private readonly List<ServerPlayer> mPlayers = new();
    private readonly ConcurrentQueue<ServerConnection> mPending = new();
    private readonly ConcurrentQueue<string> mConsoleCommands = new();

    private World mWorld = null!;
    private ServerGameContext mContext = null!;
    private TcpListener mListener = null!;
    private long mLastTimestamp;
    private int mTicksSinceSave;

    private readonly ILogSink mLog;
    private Thread? mTickThread;

    public DuncanCraftServer(ServerProperties props, ILogSink? log = null)
    {
        mProps = props;
        mLog = log ?? new ConsoleLogSink();
    }

    /// Blocks until the tick loop has exited and the final save has completed.
    public bool WaitForShutdown(TimeSpan timeout) => mTickThread?.Join(timeout) ?? true;

    /// Server-authoritative clock, advanced in Tick and broadcast by TimeUpdate. Beta's day is
    /// 24000 ticks; the client's own clock is disabled in multiplayer.
    public float TimeOfDay => mWorldTime % 24000 / 24000f;

    public void Start()
    {
        // Must precede the world: LightingEngine's constructor reads GameContext.Current for the
        // light-level clamps, so an unbound context throws inside `new World(...)`.
        mContext = new ServerGameContext(this, WorldGenSettings.Build(0));
        GameContext.Bind(mContext);

        mWorld = LoadOrCreateWorld();
        mContext.World = mWorld;

        // Everything the world changes from here on is recorded and flushed to clients each tick.
        mWorld.BeginJournalling();

        mListener = new TcpListener(IPAddress.Any, mProps.ServerPort);
        mListener.Start();

        _ = Task.Run(AcceptLoop); // accept thread

        // Not a background thread: the window's Closing handler waits on this via WaitForShutdown,
        // and a background thread would be torn down mid-save when Main returns.
        mTickThread = new Thread(TickLoop) { IsBackground = false, Name = "ServerTick" };
        mTickThread.Start();
    }

    /// Sockets accepted but not yet logged in. Beyond this we refuse rather than spawn read threads
    /// for whoever is opening connections fastest.
    private const int MAX_PENDING = 32;

    private async Task AcceptLoop()
    {
        while (Running)
        {
            TcpClient tcp;
            try
            {
                tcp = await mListener.AcceptTcpClientAsync();
            }
            catch (Exception e)
            {
                // Without this the first failed accept ended the loop for good and the server went
                // on running, silently refusing every later join.
                if (!Running) return;
                mLog.Log(LogLevel.Warning, $"Accept failed: {e.Message}");
                continue;
            }

            tcp.NoDelay = true;
            var conn = new ServerConnection(tcp);

            if (mPending.Count >= MAX_PENDING)
            {
                conn.Kick("Server is busy, try again");
                continue;
            }

            conn.Start();
            mPending.Enqueue(conn);
        }
    }

    private void TickLoop()
    {
        mLastTimestamp = Stopwatch.GetTimestamp();
        while (Running)
        {
            int due = mTicks.Accumulate(MeasureDelta());
            for (int i = 0; i < due; i++)
            {
                // One bad tick must not take the server with it: an exception here used to end the
                // tick thread outright, leaving the process alive with sockets open and nothing
                // simulating. Log it and carry on into the next tick.
                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    mLog.Log(LogLevel.Error, $"Exception in server tick: {e}");
                }
            }

            Thread.Sleep(1);
        }
    }

    private void Tick()
    {
        AcceptPendingLogins();
        foreach (var p in mPlayers.ToList())
        {
            DrainInbox(p);
        }

        while (mConsoleCommands.TryDequeue(out var cmd))
        {
            RunConsoleCommand(cmd);
        }

        mWorld.Update(Vector3.One);
        mWorld.TickEntities();
        mWorld.DoScheduledTick();
        mWorld.DoRandomTick();
        TickMobSpawning();

        // Before anything is replicated: mobs, arrows and TNT arrive with constructor ids, which
        // come from a different counter than the server's.
        AdoptNewEntities();

        foreach (var p in mPlayers)
        {
            CheckFallDamage(p);
            TickEnvironmentalDamage(p);
            if (p.HurtCooldownTicks > 0) p.HurtCooldownTicks--;
        }

        mWorld.Streamer.Update(mPlayers.Select(p => new ChunkObserver(p.EntityId, p.Position, p.ViewDistanceChunks))
            .ToList());

        foreach (var p in mPlayers)
        {
            SendChunks(p);
        }

        foreach (var o in mPlayers)
        {
            BroadcastMovement(o);
        }

        TickDroppedItems();
        TickFurnaces();

        foreach (var p in mPlayers)
        {
            TrackMobsFor(p);
            TrackDropsFor(p);
        }

        PruneDropAges();

        BroadcastEntityMovement();

        // Last, so one packet carries everything this tick did - including whatever the handlers
        // above changed.
        FlushBlockChanges();
        BroadcastTime();
        CheckKeepAliveTimeouts();

        if (mTickCount % (TickSystem.TPS * 30) == 0)
            BroadcastKeepAlive();

        DropDeadConnections();

        if (++mTicksSinceSave > TickSystem.TPS * 120)
        {
            SaveEverything();
            mTicksSinceSave = 0;
        }

        mTickCount++;
        mWorldTime++;
    }

    private float MeasureDelta()
    {
        long now = Stopwatch.GetTimestamp();
        float dt = (now - mLastTimestamp) / (float)Stopwatch.Frequency;
        mLastTimestamp = now;
        return MathF.Min(dt, .25f);
    }

    private void DrainInbox(ServerPlayer player)
    {
        const int MAX_PER_TICK = 64;

        for (int i = 0; i < MAX_PER_TICK; i++)
        {
            if (!player.Connection.Inbox.TryDequeue(out var packet)) return;

            // Any packet at all proves the connection is alive; beta's KeepAlive is only the
            // fallback for a player who is standing perfectly still.
            player.LastKeepAlive = Environment.TickCount64;

            // A handler that throws on a malformed (or malicious) body must cost that one player
            // their connection, not everyone their server.
            try
            {
                Handle(player, packet);
            }
            catch (Exception e)
            {
                mLog.Log(LogLevel.Warning, $"{player.Name}: error handling {packet.Id}: {e.Message}");
                player.Connection.Kick("Internal server error");
                return;
            }
        }
    }

    private void Handle(ServerPlayer player, Packet packet)
    {
        var r = packet.OpenBody();
        switch (packet.Id)
        {
            case PacketId.PlayerPosition:
            case PacketId.PlayerLook:
            case PacketId.PlayerPositionLook: HandleMovement(player, packet.Id, r); break;
            case PacketId.PlayerDigging: HandleDigging(player, r); break;
            case PacketId.PlayerBlockPlacement: HandlePlacement(player, r); break;
            case PacketId.ChatMessage: HandleChat(player, r.ReadString()); break;
            case PacketId.HoldingChange:
                player.HeldSlot = r.ReadShort();
                BroadcastHeldItem(player);
                break;

            // Pure relay - the swing is cosmetic, so the server just forwards it.
            case PacketId.Animation:
                BroadcastExcept(player, PacketId.Animation, w =>
                {
                    w.WriteInt(player.EntityId);
                    w.WriteByte(1);
                });
                break;

            case PacketId.UseEntity: HandleUseEntity(player, r); break;
            case PacketId.Respawn: HandleRespawn(player, r); break;

            case PacketId.WindowClick: HandleWindowClick(player, r); break;
            case PacketId.CloseWindow: HandleCloseWindow(player, r); break;

            case PacketId.KeepAlive: break;
            default: break; // unknown ids are ignored, never fatal
        }
    }

    internal void SendTo(ServerPlayer p, PacketId id, Action<NetStream> write) => p.Connection.Send(id, write);

    internal void Broadcast(PacketId id, Action<NetStream> write)
    {
        foreach (var p in mPlayers) p.Connection.Send(id, write);
    }

    internal void BroadcastExcept(ServerPlayer except, PacketId id, Action<NetStream> write)
    {
        foreach (var p in mPlayers)
        {
            if (p != except)
                p.Connection.Send(id, write);
        }
    }

    /// Beta's KeepAlive carries no payload - it exists so a dead TCP connection surfaces as a write
    /// failure instead of sitting there looking healthy until the player tries to move.
    private void BroadcastKeepAlive() => Broadcast(PacketId.KeepAlive, _ => { });

    /// Everyone who currently holds the chunk containing this block.
    internal IEnumerable<ServerPlayer> ViewersOf(Vector3i blockPos)
    {
        var coord = ChunkCoord.FromWorldBlock(blockPos.X, blockPos.Z);
        return mPlayers.Where(p => p.SentChunks.Contains(coord));
    }

    private Task mSaveInFlight = Task.CompletedTask;

    /// <param name="blocking">Shutdown path: the process is about to exit, so the write has to
    /// finish before we return rather than racing Main.</param>
    private void SaveEverything(bool blocking = false)
    {
        // A save that overran the 120s interval would otherwise have a second writer opening the
        // same chunk files behind it.
        if (!mSaveInFlight.IsCompleted && !blocking)
            return;

        mLog.Log(LogLevel.Info, "Saving chunks");

        // Snapshot on the tick thread, write on a worker - never block the tick on disk. The dirty
        // flags are cleared HERE, before the worker starts, so edits made while it writes aren't
        // swallowed by a flag the worker clears later.
        var dirty = mWorld.LoadedChunks.Where(c => c.HasChunkBeenModified).ToList();
        foreach (var c in dirty)
        {
            c.HasChunkBeenModified = false;
        }

        var snapshot = mPlayers.Select(SnapshotPlayer).ToList();

        void Write()
        {
            try
            {
                foreach (var c in dirty)
                {
                    Serialization.SaveChunk(c);
                }

                foreach (var s in snapshot)
                {
                    WritePlayerFile(s);
                }
            }
            catch (Exception e)
            {
                // An unobserved exception here used to be swallowed by the Task and lose the save
                // without a word.
                mLog.Log(LogLevel.Error, $"Save failed: {e}");
            }
        }

        if (blocking)
        {
            // On the tick thread, inline: the window's Closing handler is waiting on this thread,
            // and it has a join timeout that a hand-off to a worker could easily overrun.
            mSaveInFlight.Wait(TimeSpan.FromSeconds(10)); // let any background save finish first
            Write();
            return;
        }

        mSaveInFlight = Task.Run(Write);
    }
}
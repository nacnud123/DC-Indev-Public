// Shared flow simulation for water and lava, following Beta 1.7.3's BlockFlowing. | DA

using VoxelEngine.Core;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Base for the flowing fluids. Water and lava used to each carry their own ad-hoc spread routine
/// in which every block was a full source, so a single bucket's worth of water spread across a
/// world forever and never pooled. This is Beta 1.7.3's model instead: the metadata nibble carries
/// a <em>level</em>, the fluid loses a level per block it travels, and it dies out once the level
/// would reach 8.
///
/// Metadata layout (4 bits, which is exactly what a cell has):
/// <list type="bullet">
/// <item>bits 0-2: level, 0 = source, 7 = the thinnest film before it disappears.</item>
/// <item>bit 3 (value 8): "falling" - this cell is fed from directly above. It renders full and
/// spreads like a source, which is what lets a waterfall feed a pool at its base.</item>
/// </list>
///
/// One <see cref="Block"/> instance is shared by the whole game (see <see cref="BlockRegistry"/>),
/// so unlike vanilla - which kept the flow scratch state in instance fields - everything here must
/// stay in locals. Two chunks flowing at once would otherwise corrupt each other's search.
/// </summary>
public abstract class BlockFluid : Block
{
    /// <summary>Levels lost per block travelled: water 1 (spreads 7), lava 2 (spreads 3).</summary>
    protected abstract int DecayPerBlock { get; }

    /// <summary>True for water, which pools into new sources between two existing ones.</summary>
    protected virtual bool FormsInfiniteSources => false;

    /// <summary>The maximum level; a fluid that would decay to this vanishes instead.</summary>
    protected const int MAX_LEVEL = 8;

    /// <summary>Set on a cell being fed from above. Renders full and spreads like a source.</summary>
    protected const int FALLING_FLAG = 8;

    public override bool IsSolid => false;
    public override bool IsBreakable => false;
    public override bool IsFluid => true;
    public override bool IsTransparent => true;

    /// <summary>
    /// How full a cell at this metadata is, as a fraction of the block. Vanilla's (level+1)/9 -
    /// a source fills 8/9, leaving the small gap at the top that makes a water surface read as a
    /// surface rather than a solid cube.
    /// </summary>
    public static float FillFraction(int metadata)
    {
        int level = (metadata & FALLING_FLAG) != 0 ? 0 : metadata & 7;
        return (level + 1) / 9f;
    }

    /// <summary>Y offset within the cell of the fluid's top surface.</summary>
    public static float SurfaceHeight(int metadata) => 1f - FillFraction(metadata);

    /// <summary>How hard a current shoves an entity along, per tick (vanilla's 0.014).</summary>
    public const float FLOW_PUSH_PER_TICK = 0.014f;

    /// <summary>
    /// The direction this cell's fluid is running, as a unit vector, or zero if it isn't running.
    /// Derived from the level gradient across the four horizontal neighbours: fluid points away
    /// from where it is deep and toward where it is shallow, which is downstream. A neighbour that
    /// is open air one level down counts as an even steeper drop, so a current accelerates toward
    /// a waterfall rather than stopping at the lip.
    /// </summary>
    public static Vector3 FlowDirection(World world, int x, int y, int z, BlockType fluid)
    {
        int here = EffectiveLevel(world, x, y, z, fluid);
        if (here < 0)
            return Vector3.Zero;

        var flow = Vector3.Zero;

        for (int dir = 0; dir < 4; dir++)
        {
            var (nx, nz) = Neighbour(x, z, dir);

            int neighbour = EffectiveLevel(world, nx, y, nz, fluid);
            if (neighbour >= 0)
            {
                int drop = neighbour - here;
                flow += new Vector3((nx - x) * drop, 0f, (nz - z) * drop);
                continue;
            }

            // Not fluid. If it's something you could fall through, look one cell down: fluid there
            // means the surface is about to drop away in this direction.
            if (BlocksFlow(world, nx, y, nz))
                continue;

            int below = EffectiveLevel(world, nx, y - 1, nz, fluid);
            if (below < 0)
                continue;

            int fallDrop = below - (here - MAX_LEVEL);
            flow += new Vector3((nx - x) * fallDrop, 0f, (nz - z) * fallDrop);
        }

        return flow.LengthSquared() > 0.0001f ? Vector3.Normalize(flow) : Vector3.Zero;
    }

    // Level for flow purposes: falling cells read as full, non-fluid reads as -1.
    private static int EffectiveLevel(World world, int x, int y, int z, BlockType fluid)
    {
        if (world.GetBlock(x, y, z) != fluid)
            return -1;

        int meta = world.GetMetadata(x, y, z);
        return (meta & FALLING_FLAG) != 0 ? 0 : meta & 7;
    }

    /// <summary>
    /// True if <paramref name="point"/> is actually inside the fluid at its cell - that is, below
    /// the surface the level implies. Without this a 1/9-deep film of water on the ground would
    /// count as being submerged in it, and you would swim across a puddle.
    /// </summary>
    public static bool ContainsPoint(World world, BlockType fluid, Vector3 point)
    {
        int bx = (int)MathF.Floor(point.X);
        int by = (int)MathF.Floor(point.Y);
        int bz = (int)MathF.Floor(point.Z);

        if (world.GetBlock(bx, by, bz) != fluid)
            return false;

        return point.Y < by + SurfaceHeight(world.GetMetadata(bx, by, bz));
    }

    /// <summary>Reaction when this fluid tries to move into a cell holding the opposing one.</summary>
    protected virtual void OnMeetOpposingFluid(World world, int x, int y, int z) { }

    /// <summary>The other fluid this one reacts with, or Air for none.</summary>
    protected virtual BlockType OpposingFluid => BlockType.Air;

    /// <summary>
    /// One flow step. Runs vanilla's order: first settle this cell's own level against its
    /// neighbours, then try to fall, and only spread sideways if falling is impossible.
    /// </summary>
    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        int level = world.GetMetadata(x, y, z);

        // --- 1. Settle this cell's level against its neighbours ---------------------------------
        // A source (level 0) is fixed and skips this: it is fed from somewhere the simulation
        // doesn't model, which is exactly what makes it a source.
        if (level > 0)
        {
            int adjacentSources = 0;
            int smallest = -100;
            smallest = SmallestNeighbourLevel(world, x - 1, y, z, smallest, ref adjacentSources);
            smallest = SmallestNeighbourLevel(world, x + 1, y, z, smallest, ref adjacentSources);
            smallest = SmallestNeighbourLevel(world, x, y, z - 1, smallest, ref adjacentSources);
            smallest = SmallestNeighbourLevel(world, x, y, z + 1, smallest, ref adjacentSources);

            int settled = smallest + DecayPerBlock;
            if (settled >= MAX_LEVEL || smallest < 0)
                settled = -1; // nothing feeding this cell any more - it drains away

            // Fed from above: inherit the falling flag rather than a decayed level, so a column of
            // falling water stays full all the way down instead of thinning out as it drops.
            int above = LevelAt(world, x, y + 1, z);
            if (above >= 0)
                settled = above >= MAX_LEVEL ? above : above + FALLING_FLAG;

            // Two adjacent sources with something solid underneath make a third. This is what
            // makes an infinite water pool possible, and it is water-only.
            if (FormsInfiniteSources && adjacentSources >= 2)
            {
                var below = world.GetBlock(x, y - 1, z);
                if (BlockRegistry.IsSolid(below) || (below == Type && world.GetMetadata(x, y - 1, z) == 0))
                    settled = 0;
            }

            if (settled != level)
            {
                if (settled < 0)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                    // Clear the level behind us: metadata is shared with facing directions, and a
                    // leftover 5 would read as a facing on whatever gets built here next.
                    world.SetMetadata(x, y, z, 0);
                    world.SetChunkAsModified(x, y, z);
                    return;
                }

                level = settled;
                world.SetMetadata(x, y, z, (byte)settled);
                world.SetChunkAsModified(x, y, z);
                ScheduleAround(world, x, y, z);
            }
        }

        // --- 2. Fall ---------------------------------------------------------------------------
        if (CanDisplace(world, x, y - 1, z))
        {
            if (world.GetBlock(x, y - 1, z) == OpposingFluid)
            {
                OnMeetOpposingFluid(world, x, y - 1, z);
                return;
            }

            // Whatever is below is fed from above, so it carries the falling flag. A cell that is
            // itself already falling passes its own level down unchanged.
            FlowInto(world, x, y - 1, z, level >= MAX_LEVEL ? level : level | FALLING_FLAG);
            return;
        }

        // --- 3. Spread sideways ----------------------------------------------------------------
        // Only a source or a cell resting on solid ground spreads outward; anything mid-fall keeps
        // falling instead, which is why a waterfall is a column and not a cone.
        if (level < 0 || (level != 0 && !BlocksFlow(world, x, y - 1, z)))
            return;

        // A falling cell spreads as though it were one step from a source, so the pool at the
        // bottom of a waterfall is fed properly rather than starting already half-decayed.
        int spreadLevel = level >= MAX_LEVEL ? DecayPerBlock : level + DecayPerBlock;
        if (spreadLevel >= MAX_LEVEL)
            return;

        var directions = OptimalFlowDirections(world, x, y, z);

        if (directions[0]) TrySpread(world, x - 1, y, z, spreadLevel);
        if (directions[1]) TrySpread(world, x + 1, y, z, spreadLevel);
        if (directions[2]) TrySpread(world, x, y, z - 1, spreadLevel);
        if (directions[3]) TrySpread(world, x, y, z + 1, spreadLevel);
    }

    // Level of the same fluid at a position, or -1 if that cell holds something else.
    private int LevelAt(World world, int x, int y, int z)
        => world.GetBlock(x, y, z) == Type ? world.GetMetadata(x, y, z) : -1;

    // Folds one neighbour into the running minimum, counting sources on the way past (the count
    // drives the infinite-source rule above). Falling neighbours count as full.
    private int SmallestNeighbourLevel(World world, int x, int y, int z, int current, ref int adjacentSources)
    {
        int level = LevelAt(world, x, y, z);
        if (level < 0)
            return current;

        if (level == 0)
            adjacentSources++;

        if (level >= MAX_LEVEL)
            level = 0;

        return current >= 0 && level >= current ? current : level;
    }

    /// <summary>True if this cell stops fluid moving through it.</summary>
    private static bool BlocksFlow(World world, int x, int y, int z)
    {
        var block = world.GetBlock(x, y, z);
        return block != BlockType.Air && BlockRegistry.IsSolid(block);
    }

    /// <summary>True if this fluid may move into the cell, washing away whatever is there.</summary>
    private bool CanDisplace(World world, int x, int y, int z)
    {
        var block = world.GetBlock(x, y, z);

        if (block == Type)
            return false;

        // Sponges hold a dry pocket around themselves.
        if (IsNearSponge(world, x, y, z))
            return false;

        // The opposing fluid is not displaced - it is reacted with, and whichever of the two ticks
        // first runs that reaction. Displacing it here would delete lava on contact instead.
        if (block == OpposingFluid)
            return true;

        return !BlocksFlow(world, x, y, z);
    }

    // Moves the fluid into a cell at the given level, destroying whatever was there.
    private void FlowInto(World world, int x, int y, int z, int level)
    {
        if (!CanDisplace(world, x, y, z))
            return;

        var existing = world.GetBlock(x, y, z);

        if (existing == OpposingFluid)
        {
            OnMeetOpposingFluid(world, x, y, z);
            return;
        }

        if (existing != BlockType.Air)
            GameContext.Current?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), existing);

        world.SetBlock(x, y, z, Type);
        world.SetMetadata(x, y, z, (byte)level);
        world.SetChunkAsModified(x, y, z);
        ScheduleAround(world, x, y, z);
    }

    // Sideways spread only overwrites cells that are empty or hold a thinner film of this fluid,
    // so two flows meeting settle instead of endlessly rewriting each other.
    private void TrySpread(World world, int x, int y, int z, int level)
    {
        int existing = LevelAt(world, x, y, z);
        if (existing >= 0)
        {
            // Already this fluid: only deepen it, never thin it.
            if (existing >= MAX_LEVEL || (existing & 7) <= level)
                return;

            world.SetMetadata(x, y, z, (byte)level);
            world.SetChunkAsModified(x, y, z);
            ScheduleAround(world, x, y, z);
            return;
        }

        FlowInto(world, x, y, z, level);
    }

    /// <summary>
    /// Picks which of the four horizontal directions the fluid actually flows in. Rather than
    /// spreading evenly, each direction is scored by how far you must travel that way before the
    /// ground drops away, and only the joint-cheapest directions are used - so water finds the
    /// edge of a cliff and runs at it, instead of creeping outward as a uniform disc.
    /// </summary>
    private bool[] OptimalFlowDirections(World world, int x, int y, int z)
    {
        Span<int> cost = stackalloc int[4];

        for (int dir = 0; dir < 4; dir++)
        {
            cost[dir] = 1000;

            var (nx, nz) = Neighbour(x, z, dir);

            // Blocked, or already a source that doesn't need feeding.
            if (BlocksFlow(world, nx, y, nz) || (world.GetBlock(nx, y, nz) == Type && world.GetMetadata(nx, y, nz) == 0))
                continue;

            // An immediate drop is free - that is where the fluid wants to go.
            cost[dir] = BlocksFlow(world, nx, y - 1, nz) ? FlowCost(world, nx, y, nz, 1, dir) : 0;
        }

        int best = cost[0];
        for (int i = 1; i < 4; i++)
            if (cost[i] < best)
                best = cost[i];

        var optimal = new bool[4];
        for (int i = 0; i < 4; i++)
            optimal[i] = cost[i] == best;

        return optimal;
    }

    // Recursive search, capped at 4 blocks out, for the distance to the nearest cell the fluid
    // could fall from. Never doubles back on the direction it arrived from.
    private int FlowCost(World world, int x, int y, int z, int distance, int cameFrom)
    {
        int best = 1000;

        for (int dir = 0; dir < 4; dir++)
        {
            if (IsOpposite(dir, cameFrom))
                continue;

            var (nx, nz) = Neighbour(x, z, dir);

            if (BlocksFlow(world, nx, y, nz) || (world.GetBlock(nx, y, nz) == Type && world.GetMetadata(nx, y, nz) == 0))
                continue;

            if (!BlocksFlow(world, nx, y - 1, nz))
                return distance; // found the drop

            if (distance >= 4)
                continue;

            int cost = FlowCost(world, nx, y, nz, distance + 1, dir);
            if (cost < best)
                best = cost;
        }

        return best;
    }

    // Direction indices: 0 = -X, 1 = +X, 2 = -Z, 3 = +Z.
    private static (int x, int z) Neighbour(int x, int z, int dir) => dir switch
    {
        0 => (x - 1, z),
        1 => (x + 1, z),
        2 => (x, z - 1),
        _ => (x, z + 1),
    };

    private static bool IsOpposite(int a, int b) => (a ^ 1) == b;

    // A level change alters what the neighbours should settle to, so they have to re-evaluate.
    // SetBlock already schedules its own neighbours; this is for the metadata-only path.
    private static void ScheduleAround(World world, int x, int y, int z)
    {
        world.ScheduleBlockTick(x, y, z);
        world.ScheduleBlockTick(x - 1, y, z);
        world.ScheduleBlockTick(x + 1, y, z);
        world.ScheduleBlockTick(x, y - 1, z);
        world.ScheduleBlockTick(x, y + 1, z);
        world.ScheduleBlockTick(x, y, z - 1);
        world.ScheduleBlockTick(x, y, z + 1);
    }

    /// <summary>
    /// Whether a sponge is close enough to keep this cell dry. Sponges hold a dry pocket around
    /// themselves, so every flow attempt has to ask.
    ///
    /// The full answer costs a 7x7x7 scan, which on an ocean is hundreds of block reads per water
    /// cell per flow tick, for a block most worlds contain none of. So the chunks the radius could
    /// reach are asked first whether they contain a sponge at all (<see cref="Chunk.SpongeCount"/>),
    /// and the scan only runs when one says yes.
    /// </summary>
    protected static bool IsNearSponge(World world, int x, int y, int z)
    {
        int r = BlockSponge.ABSORB_RADIUS;

        if (!AnySpongeInChunksAround(world, x, z, r))
            return false;

        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (world.GetBlock(x + dx, y + dy, z + dz) == BlockType.Sponge)
                return true;
        }

        return false;
    }

    // The radius is smaller than a chunk, so it spans at most a 2x2 block of chunks - the ones
    // containing its min and max corners.
    private static bool AnySpongeInChunksAround(World world, int x, int z, int radius)
    {
        int minChunkX = (x - radius) >> 4, maxChunkX = (x + radius) >> 4;
        int minChunkZ = (z - radius) >> 4, maxChunkZ = (z + radius) >> 4;

        for (int cx = minChunkX; cx <= maxChunkX; cx++)
        for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
        {
            // A chunk that isn't resident can't be holding a sponge that matters here: fluid only
            // flows inside loaded chunks anyway.
            if (world.GetChunk(cx, cz) is { SpongeCount: > 0 })
                return true;
        }

        return false;
    }
}

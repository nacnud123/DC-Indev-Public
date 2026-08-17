// A sand or gravel block in mid-fall, between leaving its cell and landing in a new one. | DA

using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// A gravity block (sand, gravel) that has lost its support and is falling. Sand used to teleport
/// straight to the bottom of the column the instant it was unsupported, which is why cave-ins
/// never read as cave-ins - there was nothing to see. This is b1.7.3's EntityFallingSand: it falls
/// under its own (deliberately weak) gravity and turns back into a block when it lands.
/// </summary>
public class FallingBlockEntity : Entity
{
    public override bool IsTargetable => false;
    public override float ShadowSize => 0.45f;

    // Vanilla falling sand accelerates at 0.04 blocks/tick^2 - half normal entity gravity. It's
    // what gives a collapsing column its slightly floaty, readable drop rather than a blink.
    private const float GRAVITY_PER_TICK = 0.04f;
    private const float DRAG = 0.98f;
    // Give up and drop as an item if it's still falling after this long (fell out of the world, or
    // into a column that never ends). Vanilla uses 100 ticks.
    private const int MAX_FALL_TICKS = 100;

    private readonly BlockType mBlock;
    private readonly IRenderHandle? mAtlas;
    private IRenderHandle? mMesh;
    private int mVertexCount;
    private int mFallTicks;

    public FallingBlockEntity(Vector3 position, BlockType block)
    {
        mBlock = block;
        Position = position;

        // Slightly under a full block, as in vanilla, so a falling block doesn't catch on the
        // walls of the one-block shaft it's dropping down.
        Width = 0.98f;
        Height = 0.98f;

        mAtlas = RenderBackend.Current.WorldAtlas;
        var arr = ItemMesh.Build(ItemStack.FromBlock(block));
        mVertexCount = arr.Length / ItemMesh.VERTEX_STRIDE;
        mMesh = RenderBackend.Current.CreateMesh(arr, mVertexCount);
    }

    /// <summary>The block this entity will become when it lands.</summary>
    public BlockType Block => mBlock;

    /// <summary>
    /// Spawns a falling block at <paramref name="pos"/> and clears the cell it came from.
    /// <paramref name="onSpawned"/> lets a host see the entity before it is ticked - the server
    /// uses it to assign the network id the entity has to have before it can be replicated.
    /// </summary>
    public static void SpawnFrom(World world, Vector3i pos, Action<FallingBlockEntity>? onSpawned = null)
    {
        var type = world.GetBlock(pos.X, pos.Y, pos.Z);
        if (!BlockRegistry.IsGravityBlock(type))
            return;

        world.SetBlock(pos.X, pos.Y, pos.Z, BlockType.Air);
        world.SetChunkAsModified(pos.X, pos.Y, pos.Z);

        // Centred on the cell horizontally; Position is the feet, so Y is the cell's own floor.
        var entity = new FallingBlockEntity(new Vector3(pos.X + 0.5f, pos.Y, pos.Z + 0.5f), type);
        onSpawned?.Invoke(entity);
        world.AddEntity(entity);
    }

    /// <summary>
    /// Turns any unsupported gravity blocks stacked directly above <paramref name="pos"/> into
    /// falling entities. Called after a block is removed, so a whole column collapses one entity
    /// per cell rather than being rewritten in place.
    /// </summary>
    public static void CollapseColumnAbove(World world, Vector3i pos, Action<FallingBlockEntity>? onSpawned = null)
    {
        int y = pos.Y + 1;
        while (BlockRegistry.IsGravityBlock(world.GetBlock(pos.X, y, pos.Z)))
        {
            SpawnFrom(world, new Vector3i(pos.X, y, pos.Z), onSpawned);
            y++;
        }
    }

    public override void Tick(World world)
    {
        const float dt = TickSystem.TICK_DURATION;

        // Per-tick maths, like the player's - Velocity stays in blocks/second for everyone else.
        Vector3 motion = Velocity * dt;
        motion.Y -= GRAVITY_PER_TICK;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), motion);
        Position += actual;

        motion *= DRAG;
        Velocity = motion / dt;

        bool landed = MathF.Abs(actual.Y) < MathF.Abs(motion.Y) * 0.99f && motion.Y < 0f;

        if (!landed && ++mFallTicks < MAX_FALL_TICKS)
            return;

        IsAlive = false;

        // Land in the cell the entity's own centre now sits in, so it stacks on whatever stopped it.
        int bx = (int)MathF.Floor(Position.X);
        int by = (int)MathF.Floor(Position.Y + 0.5f);
        int bz = (int)MathF.Floor(Position.Z);

        var occupying = world.GetBlock(bx, by, bz);
        bool canPlace = landed && (occupying == BlockType.Air || BlockRegistry.Get(occupying).IsReplaceable);

        if (canPlace)
        {
            world.SetBlock(bx, by, bz, mBlock);
            world.SetChunkAsModified(bx, by, bz);
            GameContext.Current.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(mBlock));
            return;
        }

        // Nowhere to settle (landed in lava, or the timeout fired): fall back to an item drop so
        // the block isn't silently destroyed.
        var drop = BlockRegistry.GetDrop(mBlock, 0);
        if (drop.HasValue)
            world.AddEntity(new DroppedItemEntity(Position, drop.Value));
    }

    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        if (mMesh == null)
            return;

        // ItemMesh builds the cube in 0..1 local space, so shift it back by half a block to centre
        // it on the entity's X/Z while leaving its base at the entity's feet.
        Matrix4x4 mvp =
            Matrix4x4.CreateTranslation(-0.5f, 0f, -0.5f)
            * Matrix4x4.CreateTranslation(Position)
            * view
            * projection;

        RenderBackend.Current.DrawMesh(mMesh, mAtlas, mvp);
    }

    public override void Dispose()
    {
        mMesh?.Dispose();
        mMesh = null;
    }
}

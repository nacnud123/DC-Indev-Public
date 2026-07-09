// Main block parent class, holds a lot of block properties, some of which are not used yet | DA | 2/5/26

using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

// Base class every block type (BlockGrass, BlockStone, BlockTNT, ...) inherits from. Most properties below are "virtual" with a default value, meaning a subclass only needs to override the ones it wants to change - e.g. BlockStone just overrides Hardness, it doesn't need to re-specify IsSolid, ShowInInventory, etc. One Block instance is shared for the whole game (see BlockRegistry), it does NOT represent a single placed block - the world only stores which BlockType is at each position.
public abstract class Block
{
    // Fallback UV rect (atlas tile 0,0) used by any texture property a subclass doesn't override.
    private static readonly TextureCoords KDefaultCoords = UvHelper.FromTileCoords(0, 0);

    /// <summaBlockAirry>
    /// The BlockType enum value this instance represents. Used as the lookup key in BlockRegistry and is the only thing actually stored per-block in a Chunk's byte array.
    /// </summary>
    public abstract BlockType Type { get; }

    /// <summary>
    /// Tells ChunkMeshBuilder how to mesh this block: a full opaque cube (Normal), a cross-sprite (flowers/saplings/mushrooms), a liquid surface, a slab/stair partial cube, etc. Changing this changes which mesh-generation code path the block goes through.
    /// </summary>
    public virtual RenderingType RenderType => RenderingType.Normal;

    /// <summary>
    /// Selects the break-particle/sound set (wood, stone, glass, wool, dirt, ...) played when the block is mined or destroyed. Purely cosmetic - does not affect mining speed.
    /// </summary>
    public virtual BlockBreakMaterial BreakMaterial => BlockBreakMaterial.None;

    /// <summary>
    /// Display name shown in inventory tooltips/UI.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Whether entities collide with this block (false = walk/swim through it, e.g. air, water, tall grass).
    /// </summary>
    public virtual bool IsSolid => true;
    
    /// <summary>
    /// If true, the block obeys gravity and spawns a falling-block entity when unsupported (sand, gravel).
    /// </summary>
    public virtual bool GravityBlock => false;
    
    /// <summary>
    /// If false, the block cannot be mined at all (e.g. bedrock).
    /// </summary>
    public virtual bool IsBreakable => true;
    
    /// <summary>
    /// If true, placing a block here replaces this one instead of failing (e.g. tall grass, water).
    /// </summary>
    public virtual bool IsReplaceable => false;
    
    /// <summary>
    /// Whether the block appears as a selectable item in the creative/survival inventory (some blocks, like FurnaceLit or DoubleChest, are internal-only and hide themselves here).
    /// </summary>
    public virtual bool ShowInInventory => true;
    
    /// <summary>
    /// Whether this block fully blocks sky/block light propagation. Derived from LightOpacity by default (opacity 15 = fully opaque); affects LightingEngine's flood-fill and ChunkMeshBuilder's face-culling (a face between two blocks that both BlocksLight is never emitted).
    /// </summary>
    public virtual bool BlocksLight => LightOpacity >= 15;
    
    /// <summary>
    /// Inverse of BlocksLight; used by mesh building to decide whether a neighboring face should be rendered (transparent neighbors don't cull the shared face).
    /// </summary>
    public virtual bool IsTransparent => !BlocksLight;
    
    /// <summary>
    /// If true, entities standing where this block would be placed take suffocation damage (used for solid opaque blocks that occupy the entity's head).
    /// </summary>
    public virtual bool SuffocatesBeneath => false;
    
    /// <summary>
    /// If true, this block breaks/pops off if the block directly beneath it is removed (e.g. torches, saplings, flowers - anything that needs a solid block underneath).
    /// </summary>
    public virtual bool NeedsSupportBelow => false;
    
    /// <summary>For cross-sprite render types: whether the two crossed quads are offset from the block's
    /// center (natural-looking foliage) or aligned exactly through it.</summary>
    public virtual bool CrossHasOffset => true;
    
    /// <summary>
    /// Light level (0-15, Minecraft-style) this block emits, feeding LightingEngine's block-light propagation. 0 = no light (most blocks); 15 = full brightness (e.g. glowstone).
    /// </summary>
    public virtual int LightEmission => 0;
    
    /// <summary>
    /// How much this block attenuates light passing through it (0-15). 15 = fully opaque (default, blocks all light); lower values (e.g. glass = 2) let some light through and drive BlocksLight.
    /// </summary>
    public virtual int LightOpacity => 15;
    
    /// <summary>
    /// Base mining time factor - higher takes longer to break. Combined with PreferredTool/MinimumTier by the mining-speed calculation elsewhere (Player.Interaction.cs).
    /// </summary>
    public virtual float Hardness => 1.0f;
    
    /// <summary>
    /// If true, this block is a candidate for World's random per-chunk tick (crop growth, sponge absorption, leaf decay, etc.) - see RandomTick.
    /// </summary>
    public virtual bool TicksRandomly => false;
    
    /// <summary>
    /// Whether fire can spread to/consume this block.
    /// </summary>
    public virtual bool IsFlamable => false;
    /// <summary>
    /// Divides down how often TicksRandomly blocks actually receive a random tick (1 = every eligible tick; higher values make the effect rarer without changing the tick loop itself).
    /// </summary>
    public virtual int RandomTickDivisor => 1;
    
    /// <summary>
    /// Whether this is a liquid (water/lava) - drives flow simulation and swim physics rather than static block placement rules.
    /// </summary>
    public virtual bool IsFluid => false;
    
    /// <summary>
    /// Whether entities moving through this block have their movement speed reduced (e.g. water, cobweb).
    /// </summary>
    public virtual bool SlowsEntities => false;
    
    /// <summary>
    /// Whitelist of blocks this block is allowed to rest on top of; checked by CanBlockSupport. Defaults to BlockType.All (can be placed on anything).
    /// </summary>
    public virtual List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.All };

    /// <summary>
    /// Local-space AABB min/max (0..1 per axis) used for collision and selection box, lets slabs, stairs, etc. occupy less than a full block cell without a different collision system.
    /// </summary>
    public virtual Vector3 BoundsMin => Vector3.Zero;
    public virtual Vector3 BoundsMax => Vector3.One;

    // Per-face atlas UV rects. Side faces (Front/Back/Left/Right) default to SideTextureCoords so a simple block only needs to set Top/Bottom/Side; blocks with distinct faces per direction (furnace front, chest front, logs, etc.) override the individual face directly.
    public virtual TextureCoords TopTextureCoords => KDefaultCoords;
    /// <summary>
    /// Metadata-aware variant of TopTextureCoords, used by blocks whose top face changes appearance based on stored metadata (e.g. crop growth stage). Defaults to the static top texture.
    /// </summary>
    public virtual TextureCoords GetTopTexture(byte metadata) => TopTextureCoords;
    public virtual TextureCoords BottomTextureCoords => KDefaultCoords;

    public virtual TextureCoords FrontTextureCoords => SideTextureCoords;
    public virtual TextureCoords BackTextureCoords => SideTextureCoords;
    public virtual TextureCoords LeftTextureCoords => SideTextureCoords;
    public virtual TextureCoords RightTextureCoords => SideTextureCoords;



    public virtual TextureCoords SideTextureCoords => KDefaultCoords;
    /// <summary>
    /// Texture used for the inventory icon and break/dig particles; defaults to the top face.
    /// </summary>
    public virtual TextureCoords InventoryTextureCoords => TopTextureCoords;

    /// <summary>
    /// Interval (in ticks) at which World schedules a guaranteed ScheduledTick call for this block (e.g. fluid flow update, redstone-like propagation). 0 means the block is never scheduled this way (only RandomTick/TicksRandomly applies, if enabled).
    /// </summary>
    public virtual int TickRate => 0;  // 0 = not scheduled

    /// <summary>
    /// Called client-side purely for cosmetic per-frame particle effects (e.g. smoke from furnaces/torches); has no effect on world state.
    /// </summary>
    public virtual void RandomDisplayTick(int x, int y, int z, Random random) { }
    /// <summary>
    /// Called occasionally (subject to TicksRandomly/RandomTickDivisor) for gameplay logic like crop growth, leaf decay, or sponge absorption. May mutate the world.
    /// </summary>
    public virtual void RandomTick(World world, int x, int y, int z, Random random) { }
    /// <summary>
    /// Called at the fixed TickRate cadence when a tick has been explicitly scheduled for this position (e.g. via World.ScheduleBlockTick) - used for deterministic updates like fluid flow.
    /// </summary>
    public virtual void ScheduledTick(World world, int x, int y, int z, Random random) { }
    
    /// <summary>
    /// Fired immediately after this block is placed into the world. Override for one-shot setup logic (e.g. BlockSponge absorbing nearby fluids on placement).
    /// </summary>
    public virtual void OnPlaced(World world, int x, int y, int z) { }
    
    /// <summary>
    /// Fired immediately after this block is removed/destroyed (mined, exploded, burned). Override for cleanup or chained effects (e.g. BlockTNT spawning its TntEntity here).
    /// </summary>
    public virtual void OnRemoved(World world, int x, int y, int z) { }
    
    /// <summary>
    /// Fired each tick an entity is standing on/in this block's space (e.g. for damage-over-time or triggering effects like pressure plates).
    /// </summary>
    public virtual void OnEntityWalking(World world, int x, int y, int z, Random random) { }

    /// <summary>Whether this block type is allowed to rest on top of the given block, per BlocksThatCanSupport.</summary>
    public virtual bool CanBlockSupport(BlockType beneath)
    {
        var needed = BlocksThatCanSupport;
        if (needed.Contains(BlockType.All))
            return true;

        return needed.Contains(beneath);
    }

    /// <summary>
    /// Item stack dropped when this block is broken. Defaults to a stack of the block's own item form; override to return null (no drop) or a different item/count (e.g. ore -> raw material).
    /// </summary>
    public virtual ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(this.Type);
    /// <summary>Tool category that mines this block at normal speed (Pickaxe, Axe, Shovel, ...).</summary>
    public virtual ToolType PreferredTool => ToolType.None;
    /// <summary>Minimum tool tier (Wood/Stone/Iron/Diamond/...) required to get a drop at all from this block.</summary>
    public virtual ToolTier MinimumTier => ToolTier.None;
    public virtual int DropCount => 1;
    public virtual int MaxStackSize => 64;

    // Any block can call this to become "explosive": it replaces the block with a ticking TntEntity (a falling, flashing cube) that blows up after fuseDuration seconds. See BlockTNT.OnRemoved for the block that actually uses this.
    public void Explode(World world, int x, int y, int z, float fuseDuration = 4.0f, int explosionRadius = 4,
        float explosionPower = 4f)

    {
        // No active Game instance (e.g. headless/tooling context) means no texture/entity system to spawn into, so silently no-op rather than throwing.
        if (Game.Instance == null)
            return;

        var tnt = new TntEntity(new Vector3(x, y, z), Game.Instance.WorldTexture, this, fuseDuration, explosionRadius, explosionPower);
        world.AddEntity(tnt);
    }
}

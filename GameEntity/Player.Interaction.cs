// A partial class that handles breaking and placing blocks | DA | 2/14/26
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public partial class Player
{
    private float mBreakProgress;
    private Vector3i? mBreakingPos;
    private float mBreakHardness;
    private float mBreakCooldown;
    private float mDigSoundTimer;

    public Vector3i? BreakingBlockPos => mBreakingPos;

    // Main function that handles breaking blocks, mostly doing block breaking progress. Also, plays sounds
    public void UpdateBreaking(World world, float dt, bool holdingAttack)
    {
        if (mBreakCooldown > 0f)
        {
            mBreakCooldown -= dt;
            return;
        }

        var hit = world.Raycast(Camera.Position, Camera.Front);
        bool hitBlock = hit.Type == RaycastHitType.Block;

        // Reset if not holding, no block hit, or target changed
        if (!holdingAttack || !hitBlock || (mBreakingPos.HasValue && hit.BlockPos != mBreakingPos.Value))
        {
            ResetBreakProgress();
            if (!holdingAttack || !hitBlock)
                return;
        }

        var blockPos = hit.BlockPos;
        var blockType = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);

        if (!BlockRegistry.IsBreakable(blockType))
            return;
        
        float hardness = BlockRegistry.GetHardness(blockType);

        // Instant break for zero-hardness blocks and when the player is flying
        if (hardness <= 0f || this.IsFlying)
        {
            BreakBlock(world, blockPos);
            mBreakCooldown = 0.15f;
            return;
        }

        // Start or continue breaking
        if (!mBreakingPos.HasValue)
        {
            mBreakingPos = blockPos;
            mBreakHardness = hardness;
            mBreakProgress = 0f;
        }

        mBreakProgress += dt;
        mDigSoundTimer -= dt;

        if (mDigSoundTimer <= 0f)
        {
            mDigSoundTimer = 0.25f;
            Game.Instance.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(blockType));
        }

        if (mBreakProgress >= mBreakHardness)
        {
            BreakBlock(world, blockPos);
            mBreakCooldown = 0.15f;
            ResetBreakProgress();
        }
    }

    // Returns the stage that the block breaking is in, used to draw the right block breaking state texture
    public int GetBreakStage()
    {
        if (!mBreakingPos.HasValue || mBreakHardness <= 0f)
            return -1;

        float ratio = mBreakProgress / mBreakHardness;
        int stage = (int)(ratio * 7f);
        return Math.Clamp(stage, 0, 6);
    }

    private void ResetBreakProgress()
    {
        mBreakProgress = 0f;
        mBreakingPos = null;
        mBreakHardness = 0f;
        mDigSoundTimer = 0f;
    }

    // Actually break the block and play the particles
    private void BreakBlock(World world, Vector3i blockPos)
    {
        var blockType = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
        world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Air);
        Game.Instance.ParticleSystem.SpawnBlockBreakParticles(blockPos, blockType);
        Game.Instance.AudioManager.PlayBlockBreakSound(BlockRegistry.GetBlockBreakMaterial(blockType));

        // Handle gravity blocks above
        var checkY = blockPos.Y + 1;
        while (BlockRegistry.IsGravityBlock(world.GetBlock(blockPos.X, checkY, blockPos.Z)))
        {
            world.SetBlock(blockPos.X, checkY - 1, blockPos.Z, world.GetBlock(blockPos.X, checkY, blockPos.Z));
            world.SetBlock(blockPos.X, checkY, blockPos.Z, BlockType.Air);
            checkY += 1;
        }
    }

    // Main function that is called when you try to break or place a block. Breaking blocks has been moved mostly to another function to this really just handles placing blocks now.
    public void HandleBlockInteraction(World world, bool breakBlock, bool placeBlock)
    {
        var hit = world.Raycast(Camera.Position, Camera.Front);
        if (hit.Type != RaycastHitType.Block)
            return;

        var blockPos = hit.BlockPos;
        var placePos = hit.PlacePos;

        if (breakBlock)
        {
            var tempBlock = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);

            if (!BlockRegistry.IsBreakable(tempBlock))
                return;

            BreakBlock(world, blockPos);
        }
        else if (placeBlock && placePos.HasValue)
        {
            var placeBMin = BlockRegistry.GetBoundsMin(mSelectedBlock);
            var placeBMax = BlockRegistry.GetBoundsMax(mSelectedBlock);
            var placeVec = new Vector3(placePos.Value.X, placePos.Value.Y, placePos.Value.Z);
            Aabb blockBox = new Aabb(placeVec + placeBMin, placeVec + placeBMax);
            if (!GetBoundingBox().Intersects(blockBox) || !BlockRegistry.IsSolid(mSelectedBlock))
            {
                int x = placePos.Value.X, y = placePos.Value.Y, z = placePos.Value.Z;
                var blockToPlace = mSelectedBlock;

                Vector3i SupportBlockOffset = Vector3i.Zero;

                if (blockToPlace == BlockType.Torch)
                {
                    if (world.GetBlock(x, y, z) == BlockType.Water)
                        return;

                    var diff = placePos.Value - blockPos;

                    if (diff.Y == -1)
                        return;

                    if (diff.Y == 1)
                    {
                        if (!BlockRegistry.IsSolid(world.GetBlock(x, y - 1, z)))
                            return;
                    }
                    else
                    {
                        if (diff.X == 1)
                        {
                            blockToPlace = BlockType.TorchWest;
                            SupportBlockOffset.X = -1;
                        }
                        else if (diff.X == -1)
                        {
                            blockToPlace = BlockType.TorchEast;
                            SupportBlockOffset.X = 1;
                        }
                        else if (diff.Z == 1)
                        {
                            blockToPlace = BlockType.TorchNorth;
                            SupportBlockOffset.Z = -1;
                        }
                        else if (diff.Z == -1)
                        {
                            blockToPlace = BlockType.TorchSouth;
                            SupportBlockOffset.Z = 1;
                        }

                        if (!BlockRegistry.IsSolid(world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z)))
                            return;
                    }
                }
                else if (BlockRegistry.NeedsSupportBelow(blockToPlace))
                {
                    if (!BlockRegistry.IsSolid(world.GetBlock(x, y - 1, z)))
                        return;
                }

                if (BlockRegistry.IsGravityBlock(blockToPlace))
                {
                    while (y > 0 && world.GetBlock(x, y - 1, z) == BlockType.Air)
                    {
                        y--;
                    }
                }

                var existing = world.GetBlock(x, y, z);
                if (existing != BlockType.Air && existing != BlockType.Water)
                    return;

                if (world.GetBlock(x, y - 1, z) == BlockType.Grass && !BlockRegistry.GetSuffocatesBeneath(blockToPlace))
                {
                    world.SetBlock(x, y - 1, z, BlockType.Dirt);
                }

                if (blockToPlace != BlockType.TorchEast && blockToPlace != BlockType.TorchWest && blockToPlace != BlockType.TorchNorth && blockToPlace != BlockType.TorchSouth)
                    SupportBlockOffset.Y = -1;

                if (!BlockRegistry.CanBlockSupport(blockToPlace, world.GetBlock(x + SupportBlockOffset.X, y + SupportBlockOffset.Y, z + SupportBlockOffset.Z)))
                    return;

                world.SetBlock(x, y, z, blockToPlace);
                Game.Instance.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(blockToPlace));
            }
        }
    }

    public BlockType SelectedBlock
    {
        get => mSelectedBlock;
        set => mSelectedBlock = value;
    }
}

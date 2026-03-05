// A partial class that handles breaking and placing blocks | DA | 2/14/26

using OpenTK.Mathematics;
using VoxelEngine.BlockEntities;
using VoxelEngine.Core;
using VoxelEngine.Items;
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

        var tool = GetHeldTool();
        bool correctTool = tool != null && tool.ToolType == BlockRegistry.Get(blockType).PreferredTool;

        // damage per tick = toolStrength / hardness / divisor (20 ticks/sec)
        float toolStrength = correctTool ? tool!.MiningSpeed : 1f;
        float divisor = correctTool ? 30f : (tool != null ? 100f : 30f);
        float damagePerSecond = toolStrength / hardness / divisor * 20f;

        if (IsUnderWater)
            damagePerSecond *= 0.2f;

        if (!IsOnGround)
            damagePerSecond *= 0.2f;

        // Start or continue breaking
        if (!mBreakingPos.HasValue)
        {
            mBreakingPos = blockPos;
            mBreakHardness = 1f;
            mBreakProgress = 0f;
        }

        mBreakProgress += damagePerSecond * dt;
        mDigSoundTimer -= dt;

        if (mDigSoundTimer <= 0f)
        {
            mDigSoundTimer = 0.25f;
            Game.Instance.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(blockType));
        }

        if (mBreakProgress >= 1f)
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

        // Double slabs break into single slabs instead of air
        if (blockType == BlockType.DoubleStoneslab)
        {
            world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Stoneslab);
            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(blockPos, blockType);
            Game.Instance.AudioManager.PlayBlockBreakSound(BlockRegistry.GetBlockBreakMaterial(blockType));
            world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
            return;
        }

        if (blockType == BlockType.DoubleWoodSlab)
        {
            world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.WoodSlab);
            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(blockPos, blockType);
            Game.Instance.AudioManager.PlayBlockBreakSound(BlockRegistry.GetBlockBreakMaterial(blockType));
            world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
            return;
        }

        if (blockType == BlockType.Furnace || blockType == BlockType.FurnaceLit)
        {
            if (Game.Instance.CurrentState == GameState.Furnace)
                Game.Instance.CloseFurnace();
            
            BlockEntityManager.DestroyAt(blockPos, world);
        }

        if (blockType == BlockType.Chest)
        {
            if (Game.Instance.CurrentState == GameState.Chest)
                Game.Instance.CloseChest();
            
            BlockEntityManager.DestroyAt(blockPos, world);
        }

        byte meta = (byte)world.GetMetadata(blockPos.X, blockPos.Y, blockPos.Z);
        world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Air);
        Game.Instance.ParticleSystem.SpawnBlockBreakParticles(blockPos, blockType);
        Game.Instance.AudioManager.PlayBlockBreakSound(BlockRegistry.GetBlockBreakMaterial(blockType));

        var tool = GetHeldTool();
        var minTier = BlockRegistry.Get(blockType).MinimumTier;
        bool tierMet = minTier == ToolTier.None || (tool != null && tool.ToolTier >= minTier);

        if (tool != null)
        {
            int slotIndex = PlayerInventory.HOTBAR_START + (Game.Instance.Hotbar?.SelectedSlotIndex ?? 0);
            Game.Instance.PlayerInventory?.DamageTool(slotIndex);
        }

        var drop = tierMet ? BlockRegistry.GetDrop(blockType, meta) : null;
        if (drop.HasValue)
        {
            var rng = Game.Instance.GameRandom;
            float spawnX = blockPos.X + (float)rng.NextDouble() * 0.7f + 0.15f;
            float spawnY = blockPos.Y + (float)rng.NextDouble() * 0.7f + 0.15f;
            float spawnZ = blockPos.Z + (float)rng.NextDouble() * 0.7f + 0.15f;
            world.AddEntity(new DroppedItemEntity(new Vector3(spawnX, spawnY, spawnZ), drop.Value,
                Game.Instance.WorldTexture));
        }

        // Handle gravity blocks above
        var checkY = blockPos.Y + 1;
        while (BlockRegistry.IsGravityBlock(world.GetBlock(blockPos.X, checkY, blockPos.Z)))
        {
            world.SetBlock(blockPos.X, checkY - 1, blockPos.Z, world.GetBlock(blockPos.X, checkY, blockPos.Z));
            world.SetBlock(blockPos.X, checkY, blockPos.Z, BlockType.Air);
            checkY += 1;
        }

        world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
    }

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

            if (tempBlock == BlockType.WorkBench && Game.Instance.CurrentState == GameState.Crafting)
                Game.Instance.CloseCrafting();

            if (tempBlock == BlockType.Chest && Game.Instance.CurrentState == GameState.Chest)
                Game.Instance.CloseChest();

            BreakBlock(world, blockPos);
        }
        else if (placeBlock)
        {
            var hitBlock = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);

            if (hitBlock == BlockType.WorkBench)
            {
                Game.Instance.OpenCrafting();
                return;
            }

            if (hitBlock == BlockType.Furnace || hitBlock == BlockType.FurnaceLit)
            {
                Game.Instance.OpenFurnace(blockPos);
                return;
            }

            if (hitBlock == BlockType.Chest)
            {
                Game.Instance.OpenChest(blockPos);
                return;
            }

            if (!placePos.HasValue || mSelectedBlock == BlockType.Air) 
                return;

            // If the targeted block is replaceable (flower, grass tuft, etc.), place into it directly.
            if (BlockRegistry.Get(hitBlock).IsReplaceable)
                placePos = blockPos;
            
            if (mSelectedBlock == BlockType.Stoneslab && hitBlock == BlockType.Stoneslab)
            {
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.DoubleStoneslab);
                Game.Instance.AudioManager.PlayBlockContactSound(
                    BlockRegistry.GetBlockBreakMaterial(BlockType.Stoneslab));
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                ConsumeSelectedHotbarItem();
                return;
            }

            if (mSelectedBlock == BlockType.WoodSlab && hitBlock == BlockType.WoodSlab)
            {
                world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.DoubleWoodSlab);
                Game.Instance.AudioManager.PlayBlockContactSound(
                    BlockRegistry.GetBlockBreakMaterial(BlockType.WoodSlab));
                world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
                ConsumeSelectedHotbarItem();
                return;
            }

            var placeBMin = BlockRegistry.GetBoundsMin(mSelectedBlock);
            var placeBMax = BlockRegistry.GetBoundsMax(mSelectedBlock);
            var placeVec = new Vector3(placePos.Value.X, placePos.Value.Y, placePos.Value.Z);
            Aabb blockBox = new Aabb(placeVec + placeBMin, placeVec + placeBMax);
            if (!GetBoundingBox().Intersects(blockBox) || !BlockRegistry.IsSolid(mSelectedBlock))
            {
                int x = placePos.Value.X, y = placePos.Value.Y, z = placePos.Value.Z;
                var blockToPlace = mSelectedBlock;
                byte placeMeta = 0;
                bool isWallTorch = false;

                Vector3i SupportBlockOffset = Vector3i.Zero;

                if (blockToPlace == BlockType.Torch)
                {
                    if (world.GetBlock(x, y, z) == BlockType.Water)
                        return;

                    var diff = placePos.Value - blockPos;

                    if (diff.Y == -1)
                        return;

                    if (diff.Y == 1 || diff == Vector3i.Zero)
                    {
                        // Ground torch (metadata 0) — also handles placing into a replaceable block.
                        if (!BlockRegistry.IsSolid(world.GetBlock(x, y - 1, z)))
                            return;
                    }
                    else
                    {
                        // Wall torch: metadata 1=North, 2=South, 3=East, 4=West
                        if (diff.X == 1)
                        {
                            placeMeta = 4; // West
                            SupportBlockOffset.X = -1;
                        }
                        else if (diff.X == -1)
                        {
                            placeMeta = 3; // East
                            SupportBlockOffset.X = 1;
                        }
                        else if (diff.Z == 1)
                        {
                            placeMeta = 1; // North
                            SupportBlockOffset.Z = -1;
                        }
                        else if (diff.Z == -1)
                        {
                            placeMeta = 2; // South
                            SupportBlockOffset.Z = 1;
                        }

                        isWallTorch = true;

                        if (!BlockRegistry.IsSolid(world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z)))
                            return;
                    }
                }
                else if (BlockRegistry.GetRenderType(blockToPlace) == RenderingType.Stair
                         || blockToPlace == BlockType.Furnace
                         || blockToPlace == BlockType.Chest)
                {
                    // Facing: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X)
                    var front = Camera.Front;
                    float absX = MathF.Abs(front.X);
                    float absZ = MathF.Abs(front.Z);

                    if (absX > absZ)
                        placeMeta = front.X > 0 ? (byte)2 : (byte)3;
                    else
                        placeMeta = front.Z > 0 ? (byte)1 : (byte)0;
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
                bool existingIsReplaceable = BlockRegistry.Get(existing).IsReplaceable;
                if (existing != BlockType.Air && existing != BlockType.Water && existing != BlockType.Lava &&
                    !existingIsReplaceable)
                    return;

                if (existingIsReplaceable)
                {
                    BlockRegistry.Get(existing).OnRemoved(world, x, y, z);
                    Game.Instance.ParticleSystem.SpawnBlockBreakParticles(new OpenTK.Mathematics.Vector3i(x, y, z),
                        existing);
                }

                if (world.GetBlock(x, y - 1, z) == BlockType.Grass && !BlockRegistry.GetSuffocatesBeneath(blockToPlace))
                {
                    world.SetBlock(x, y - 1, z, BlockType.Dirt);
                }

                if (!isWallTorch)
                    SupportBlockOffset.Y = -1;

                if (!BlockRegistry.CanBlockSupport(blockToPlace,
                        world.GetBlock(x + SupportBlockOffset.X, y + SupportBlockOffset.Y, z + SupportBlockOffset.Z)))
                    return;

                world.SetBlock(x, y, z, blockToPlace);
                if (placeMeta != 0)
                    world.SetMetadata(x, y, z, placeMeta);
                Game.Instance.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(blockToPlace));
                world.SetChunkAsModified(x, y, z);
                ConsumeSelectedHotbarItem();
            }
        }
    }

    private void ConsumeSelectedHotbarItem()
    {
        var inv = Game.Instance.PlayerInventory;
        var hotbar = Game.Instance.Hotbar;

        if (inv == null || hotbar == null)
            return;

        inv.ConsumeOne(PlayerInventory.HOTBAR_START + hotbar.SelectedSlotIndex);
        if (!hotbar.GetSelectedStack().HasValue)
            mSelectedBlock = BlockType.Air;
    }

    public void UseHeldItem(World world, ItemType itemType)
    {
        var def = ItemRegistry.Get(itemType);

        if (def.IsFood)
        {
            if (Health >= PLAYER_MAX_HEALTH)
                return;

            Health = Math.Min(Health + def.FoodRestore, PLAYER_MAX_HEALTH);
            var inv = Game.Instance.PlayerInventory;
            var hotbar = Game.Instance.Hotbar;

            if (inv == null || hotbar == null)
                return;

            inv.ConsumeOne(PlayerInventory.HOTBAR_START + hotbar.SelectedSlotIndex);

            Game.Instance.AudioManager.PlayMunchSound();

            return;
        }

        if (def.OnUse == null)
            return;

        bool used;
        if (def.SkipBlockRaycast)
        {
            used = def.OnUse(world, Vector3i.Zero, null);
        }
        else
        {
            var hit = world.Raycast(Camera.Position, Camera.Front);
            if (hit.Type != RaycastHitType.Block)
                return;

            used = def.OnUse(world, hit.BlockPos, hit.PlacePos);
        }

        if (!used)
            return;

        var inv2 = Game.Instance.PlayerInventory;
        var hotbar2 = Game.Instance.Hotbar;
        if (inv2 == null || hotbar2 == null)
            return;

        int slotIndex = PlayerInventory.HOTBAR_START + hotbar2.SelectedSlotIndex;
        if (def.IsTool)
            inv2.DamageTool(slotIndex);
        else
            inv2.ConsumeOne(slotIndex);
    }

    public BlockType SelectedBlock
    {
        get => mSelectedBlock;
        set => mSelectedBlock = value;
    }

    private ItemDef? GetHeldTool()
    {
        var stack = Game.Instance.Hotbar?.GetSelectedStack();
        if (stack == null || stack.Value.IsBlock) 
            return null;
        
        var def = ItemRegistry.Get(stack.Value.Item);
        return def.IsTool ? def : null;
    }
}
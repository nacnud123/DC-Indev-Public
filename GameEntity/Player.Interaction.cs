// A partial class that handles breaking and placing blocks | DA | 2/14/26


using VoxelEngine.BlockEntities;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Player half handling world interaction: progressive block breaking (accumulated over multiple frames based on tool/hardness), instant block placement/replacement (including special-case blocks like slabs, torches, stairs, chests/double-chests), and held-item use (food, tools, placeable items). All raycasting against the world goes through World.Raycast from the camera.
/// </summary>
public partial class Player
{
    private float mBreakProgress; // accumulated "damage" toward mBreakHardness (1.0 = block destroyed)
    private Vector3i? mBreakingPos; // block coordinate currently being mined, or null if not mining
    private float mBreakHardness; // normalized target for mBreakProgress; always 1f once mining starts (see UpdateBreaking)
    private float mBreakCooldown; // seconds until another break/attack action is allowed (post-break swing delay)
    private float mDigSoundTimer; // seconds until the next periodic "digging" sound plays while mining

    public Vector3i? BreakingBlockPos => mBreakingPos;

    /// <summary>
    /// Advances progressive block-breaking. Call every frame with whether the attack/break button is currently held. Handles: resetting progress if the player releases the button, looks away, or switches target block; computing per-second break speed from tool type/mining speed vs. block hardness (Minecraft-style: damage/sec = toolStrength / hardness / divisor, divisor differs for correct-tool/wrong-tool/no-tool); underwater and airborne mining penalties (both -80% speed); periodic dig sound playback; and finally destroying the block once progress reaches 1.0.
    /// </summary>
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

        // damage per tick = toolStrength / hardness / divisor (20 ticks/sec) Divisor is smallest (fastest breaking) for the correct tool, largest for the wrong tool (penalized relative to bare hands), and a middle value for bare hands with no tool held.
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
            // mBreakHardness is a normalized target (always 1), not the block's actual hardness value — the hardness/tool math is already baked into damagePerSecond above.
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

    /// <summary>Maps current break progress to one of 7 break-stage textures (0-6) for the crack overlay, or -1 if not mining.</summary>
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

    /// <summary>
    /// Destroys the block at blockPos: handles special-case blocks that don't simply become air (double slabs revert to single slabs; furnaces/chests/double-chests close their open UI and tear down their block-entity data, dropping double-chest contents as item entities and re-registering the surviving half as a single chest), then does the generic path — clear to air, spawn break particles/sound, damage the held tool, roll an item drop (gated by tool tier meeting the block's minimum), and finally collapse any gravity blocks (sand/gravel) stacked directly above into the now-empty space.
    /// </summary>
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

        if (blockType == BlockType.DoubleChest)
        {
            if (Game.Instance.CurrentState == GameState.DoubleChest)
                Game.Instance.CloseDoubleChest();

            // A double chest's 54-slot inventory is stored once, keyed by the "canonical" half (see GetDoubleChestCanonical — the lower X, then lower Z, half). Breaking either half requires locating the canonical position to find the shared ChestData.
            var otherPos = GetDoubleChestNeighbor(world, blockPos);
            var canonicalPos = GetDoubleChestCanonical(blockPos, otherPos);
            bool breakingCanonical = blockPos == canonicalPos;

            var dc = BlockEntityManager.GetOrCreateDoubleChest(canonicalPos);
            var center = new Vector3(blockPos.X + 0.5f, blockPos.Y + 0.5f, blockPos.Z + 0.5f);

            // Slots [0,27) belong to the canonical half, [27,54) to the other half. Drop the slots belonging to whichever half is actually being broken.
            int dropStart = breakingCanonical ? 0 : 27;
            for (int i = dropStart; i < dropStart + ChestData.CHEST_SLOTS; i++)
            {
                var item = dc.GetSlot(i);
                if (item.HasValue)
                    world.AddEntity(new DroppedItemEntity(center, item.Value, Game.Instance.WorldTexture));
            }

            if (otherPos.HasValue)
            {
                // The other half survives as a regular single chest; re-key its surviving slots to a fresh single-chest ChestData starting at index 0.
                int surviveStart = breakingCanonical ? 27 : 0;
                var surviving = new ChestData(otherPos.Value);
                for (int i = 0; i < ChestData.CHEST_SLOTS; i++)
                    surviving.SetSlot(i, dc.GetSlot(surviveStart + i));

                BlockEntityManager.Remove(canonicalPos);
                BlockEntityManager.RegisterChest(otherPos.Value, surviving);
                world.SetBlock(otherPos.Value.X, otherPos.Value.Y, otherPos.Value.Z, BlockType.Chest);
                world.SetChunkAsModified(otherPos.Value.X, otherPos.Value.Y, otherPos.Value.Z);
            }
            else
            {
                BlockEntityManager.Remove(canonicalPos);
            }
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

        // Handle gravity blocks above: shift each gravity block (sand/gravel) down one position into the newly-emptied space, repeating upward as long as gravity blocks stack directly above — this instantly "collapses" a column rather than spawning falling-block entities.
        var checkY = blockPos.Y + 1;
        while (BlockRegistry.IsGravityBlock(world.GetBlock(blockPos.X, checkY, blockPos.Z)))
        {
            world.SetBlock(blockPos.X, checkY - 1, blockPos.Z, world.GetBlock(blockPos.X, checkY, blockPos.Z));
            world.SetBlock(blockPos.X, checkY, blockPos.Z, BlockType.Air);
            checkY += 1;
        }

        world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
    }

    /// <summary>
    /// Handles a single discrete left-click (break) or right-click (place/use) action against the block the player is looking at (raycast from camera). Unlike UpdateBreaking (which is progressive, called every frame while holding), this performs the action immediately — used for interactable blocks (workbench/furnace/chest UI open) and for placement, which includes special per-block-type placement rules (slabs combining into double slabs, torch wall/ground orientation and support checks, stair/furnace/chest facing from camera direction, chest-pair merging into a double chest, gravity blocks falling to rest on placement, and grass-to-dirt conversion under non-suffocating blocks).
    /// </summary>
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

            if (tempBlock == BlockType.DoubleChest && Game.Instance.CurrentState == GameState.DoubleChest)
                Game.Instance.CloseDoubleChest();

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

            if (hitBlock == BlockType.DoubleChest)
            {
                var otherPos = GetDoubleChestNeighbor(world, blockPos);
                var canonicalPos = GetDoubleChestCanonical(blockPos, otherPos);
                Game.Instance.OpenDoubleChest(canonicalPos);
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

            if (mSelectedBlock == BlockType.Chest)
            {
                // Placing a chest next to an existing single chest merges them into a double chest. Scan the 4 horizontal neighbors for an existing Chest block.
                var pp = placePos.Value;
                Vector3i[] neighbors =
                {
                    new(pp.X + 1, pp.Y, pp.Z),
                    new(pp.X - 1, pp.Y, pp.Z),
                    new(pp.X, pp.Y, pp.Z + 1),
                    new(pp.X, pp.Y, pp.Z - 1),
                };

                foreach (var neighbor in neighbors)
                {
                    if (world.GetBlock(neighbor.X, neighbor.Y, neighbor.Z) != BlockType.Chest)
                        continue;

                    var canonicalPos = GetDoubleChestCanonical(pp, neighbor);

                    // Migrate the existing single chest's items into the new merged double-chest data before the single-chest block/entity is replaced.
                    var oldChest = BlockEntityManager.GetChestIfExists(neighbor);
                    BlockEntityManager.Remove(neighbor);

                    var dc = BlockEntityManager.GetOrCreateDoubleChest(canonicalPos);
                    if (oldChest != null)
                    {
                        // The old chest's contents go into whichever half of the 54-slot double chest corresponds to its own position (canonical = slots [0,27), other = [27,54)).
                        int destStart = canonicalPos == neighbor ? 0 : 27;
                        for (int i = 0; i < ChestData.CHEST_SLOTS; i++)
                            dc.SetSlot(destStart + i, oldChest.GetSlot(i));
                    }

                    byte facing = (byte)world.GetMetadata(neighbor.X, neighbor.Y, neighbor.Z);
                    world.SetBlock(neighbor.X, neighbor.Y, neighbor.Z, BlockType.DoubleChest);
                    world.SetBlock(pp.X, pp.Y, pp.Z, BlockType.DoubleChest);
                    world.SetMetadata(neighbor.X, neighbor.Y, neighbor.Z, facing);
                    world.SetMetadata(pp.X, pp.Y, pp.Z, facing);
                    Game.Instance.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(BlockType.Chest));
                    world.SetChunkAsModified(neighbor.X, neighbor.Y, neighbor.Z);
                    world.SetChunkAsModified(pp.X, pp.Y, pp.Z);
                    ConsumeSelectedHotbarItem();
                    return;
                }
            }

            var placeBMin = BlockRegistry.GetBoundsMin(mSelectedBlock);
            var placeBMax = BlockRegistry.GetBoundsMax(mSelectedBlock);
            var placeVec = new Vector3(placePos.Value.X, placePos.Value.Y, placePos.Value.Z);
            Aabb blockBox = new Aabb(placeVec + placeBMin, placeVec + placeBMax);
            // Prevent placing a solid block inside the player's own hitbox (would trap/suffocate them); non-solid blocks (torches, slabs the player can walk through space of, etc.) are exempt from this check.
            if (!GetBoundingBox().Intersects(blockBox) || !BlockRegistry.IsSolid(mSelectedBlock))
            {
                int x = placePos.Value.X, y = placePos.Value.Y, z = placePos.Value.Z;
                var blockToPlace = mSelectedBlock;
                byte placeMeta = 0;
                bool isWallTorch = false;

                Vector3i SupportBlockOffset = Vector3i.Zero; // relative offset (from the placement position) to the block that must support this placement

                if (blockToPlace == BlockType.Torch)
                {
                    if (world.GetBlock(x, y, z) == BlockType.Water)
                        return;

                    // diff = direction from the targeted (existing) block to the empty placement cell, i.e. which face of the target block was clicked.
                    var diff = placePos.Value - blockPos;

                    if (diff.Y == -1)
                        return; // can't place a torch on the underside of a block (clicked the bottom face)

                    if (diff.Y == 1 || diff == Vector3i.Zero)
                    {
                        // Ground torch (metadata 0) - also handles placing into a replaceable block.
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
                    // Facing: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X) Facing is derived from where the player is looking (not which face was clicked), so the block faces away from the player as they place it — whichever horizontal axis (X or Z) the camera points along more strongly determines the facing.
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
                    // Simulate instant falling: walk the placement position straight down through any air until it lands on solid ground, rather than spawning a falling entity.
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
                    Game.Instance.ParticleSystem.SpawnBlockBreakParticles(new Vector3i(x, y, z),
                        existing);
                }

                // Placing a non-suffocating block (e.g. a torch or flower) on top of grass turns the grass to dirt underneath, mirroring how grass dies when covered/shadowed.
                if (world.GetBlock(x, y - 1, z) == BlockType.Grass && !BlockRegistry.GetSuffocatesBeneath(blockToPlace))
                {
                    world.SetBlock(x, y - 1, z, BlockType.Dirt);
                }

                // Wall torches already set SupportBlockOffset to point sideways at their wall (see the torch branch above); everything else supports from directly below.
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

    /// <summary>
    /// Removes one item from the currently-selected hotbar slot after a successful block placement (no-op in creative mode, which has infinite blocks). If the stack is now empty, clears mSelectedBlock to Air so further placement attempts are rejected until a new block is selected.
    /// </summary>
    private void ConsumeSelectedHotbarItem()
    {
        if (Game.Instance.IsCreative)
            return;

        var inv = Game.Instance.PlayerInventory;
        var hotbar = Game.Instance.Hotbar;

        if (inv == null || hotbar == null)
            return;

        inv.ConsumeOne(PlayerInventory.HOTBAR_START + hotbar.SelectedSlotIndex);
        if (!hotbar.GetSelectedStack().HasValue)
            mSelectedBlock = BlockType.Air;
    }

    /// <summary>
    /// Right-click handler for the currently held non-block item. Food items heal and consume directly here; other items delegate to their own Item.OnUse (which may or may not need a raycast target block — SkipBlockRaycast lets items like bows/buckets-on-nothing bypass the requirement to be looking at a block). Tools take durability damage on use; non-tool consumables are consumed one-for-one.
    /// </summary>
    public void UseHeldItem(World world, ItemType itemType)
    {
        var item = ItemRegistry.Get(itemType);

        if (item.IsFood)
        {
            if (Health >= PLAYER_MAX_HEALTH)
                return;

            Health = Math.Min(Health + item.FoodRestore, PLAYER_MAX_HEALTH);
            var inv = Game.Instance.PlayerInventory;
            var hotbar = Game.Instance.Hotbar;

            if (inv == null || hotbar == null)
                return;

            inv.ConsumeOne(PlayerInventory.HOTBAR_START + hotbar.SelectedSlotIndex);

            Game.Instance.AudioManager.PlayMunchSound();

            return;
        }

        bool used;
        if (item.SkipBlockRaycast)
        {
            used = item.OnUse(world, Vector3i.Zero, null);
        }
        else
        {
            var hit = world.Raycast(Camera.Position, Camera.Front);
            if (hit.Type != RaycastHitType.Block)
                return;

            used = item.OnUse(world, hit.BlockPos, hit.PlacePos);
        }

        if (!used)
            return;

        var inv2 = Game.Instance.PlayerInventory;
        var hotbar2 = Game.Instance.Hotbar;
        if (inv2 == null || hotbar2 == null)
            return;

        int slotIndex = PlayerInventory.HOTBAR_START + hotbar2.SelectedSlotIndex;
        if (item.IsTool)
            inv2.DamageTool(slotIndex);
        else
            inv2.ConsumeOne(slotIndex);
    }

    /// <summary>The block type the player currently has selected to place (from the hotbar); Air means nothing placeable is selected.</summary>
    public BlockType SelectedBlock
    {
        get => mSelectedBlock;
        set => mSelectedBlock = value;
    }

    /// <summary>Returns the tool item in the selected hotbar slot, or null if the slot holds a block, a non-tool item, or is empty.</summary>
    private Item? GetHeldTool()
    {
        var stack = Game.Instance.Hotbar?.GetSelectedStack();
        if (stack == null || stack.Value.IsBlock)
            return null;

        var item = ItemRegistry.Get(stack.Value.Item);
        return item.IsTool ? item : null;
    }

    /// <summary>Finds the other half of a double chest by checking the 4 horizontal neighbors of a DoubleChest block for another DoubleChest block.</summary>
    private Vector3i? GetDoubleChestNeighbor(World world, Vector3i pos)
    {
        Vector3i[] neighbors =
        {
            new(pos.X + 1, pos.Y, pos.Z),
            new(pos.X - 1, pos.Y, pos.Z),
            new(pos.X, pos.Y, pos.Z + 1),
            new(pos.X, pos.Y, pos.Z - 1),
        };

        foreach (var n in neighbors)
        {
            if (world.GetBlock(n.X, n.Y, n.Z) == BlockType.DoubleChest)
                return n;
        }

        return null;
    }

    /// <summary>
    /// Picks a deterministic "canonical" half of a double chest pair (lower X first, then lower Z as a tiebreak for chests aligned along Z) — this is the position under which the shared 54-slot ChestData is keyed in BlockEntityManager, so both halves must agree on it consistently.
    /// </summary>
    private Vector3i GetDoubleChestCanonical(Vector3i a, Vector3i? b)
    {
        if (!b.HasValue)
            return a;

        if (a.X != b.Value.X)
            return a.X < b.Value.X ? a : b.Value;

        return a.Z < b.Value.Z ? a : b.Value;
    }
}
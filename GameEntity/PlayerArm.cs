// Renders the player's first-person arm and held item. Handles block, thick-sprite item, and bare-arm display with swing, equip-dip, and walk-bob animations. | DA | 2/21/26

using System.IO;
using Silk.NET.OpenGL;

using StbImageSharp;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using Shader = VoxelEngine.Rendering.Shader;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

public class PlayerArm
{
    // Constants

    private const string ARM_MODEL = "Resources/Entities/Sheep/SheepLeg/SheepLeg.obj";
    private const string ARM_TEXTURE = "Resources/Entities/Sheep/SheepLeg/Leg.png";

    private const float ARM_FOV = 70f;
    private const float ARM_SCALE = 2f;
    private const float BLOCK_SCALE = 0.35f;
    private const float ITEM_SCALE = 0.65f;

    private const float SWING_SPEED = 4f;
    private const float EQUIP_SPEED = 5f;
    private const float EQUIP_AMOUNT = 0.5f;

    private const float BOB_SPEED = 12f;
    private const float BOB_AMOUNT_Y = 0.025f;
    private const float BOB_AMOUNT_X = 0.012f;
    private const float BOB_TILT_DEG = 3f;

    private const float SHADE_TOP = 1.0f;
    private const float SHADE_BOTTOM = 0.5f;
    private const float SHADE_FRONT = 0.8f;
    private const float SHADE_BACK = 0.8f;
    private const float SHADE_RIGHT = 0.7f;
    private const float SHADE_LEFT = 0.7f;

    private const float THICK_DEPTH = 2.0f / 16f;

    private const string VERT_SHADER = "Shaders/PlayerArmVert.glsl";
    private const string FRAG_SHADER = "Shaders/PlayerArmFrag.glsl";

    // State

    private readonly EntityModel mArmModel;
    private readonly Texture mWorldTexture;
    private readonly Texture mItemTexture;
    private readonly Shader mShader;
    private readonly uint mVao, mVbo;

    private readonly byte[,] mItemAtlasAlpha;
    private readonly int mItemAtlasTilePixels;
    private readonly byte[,] mWorldAtlasAlpha;
    private readonly int mWorldAtlasTilePixels;

    private float mSwingProgress = -1f;
    private float mBobPhase;
    private bool mSwingRequested;

    private float mEquipProgress = -1f;
    private ItemStack? mDisplayedStack;
    private ItemStack? mPendingStack;

    private ItemStack? mCachedStack;
    private float[]? mCachedMesh;

    private float mItemRotZ = 65f;
    private float mItemRotX = 6f;
    private float mItemRotY = -105f;
    private float mItemTransX = 0.805f;
    private float mItemTransY = -0.3f;
    private float mItemTransZ = -0.85f;

    private float mBlockRotZ = -0.905f;
    private float mBlockRotY = 39f;
    private float mBlockRotX = 29f;
    private float mBlockTransX = 0.492f;
    private float mBlockTransY = -0.412f;
    private float mBlockTransZ = -0.714f;

    private float mDecoRotZ = -2f;
    private float mDecoRotX = 6f;
    private float mDecoRotY = -105f;
    private float mDecoTransX = 0.805f;
    private float mDecoTransY = -0.468f;
    private float mDecoTransZ = -0.85f;


    private readonly record struct FrameAnim(
        float BobX,
        float BobY,
        float BobTilt,
        float SwingY,
        float SwingZ,
        float EquipDip);

    public PlayerArm(Texture worldTexture, Texture itemTexture, string worldAtlasPath, string itemAtlasPath)
    {
        Entity.InitShader();
        mArmModel = EntityModel.Load(ARM_MODEL, ARM_TEXTURE);

        mWorldTexture = worldTexture;
        mItemTexture = itemTexture;
        mShader = new Shader(File.ReadAllText(VERT_SHADER), File.ReadAllText(FRAG_SHADER));

        var gl = GlContext.Gl;
        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();
        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(6 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(6 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 1, GLEnum.Float, false, (uint)(6 * sizeof(float)), (nint)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        StbImage.stbi_set_flip_vertically_on_load(0);

        ImageResult itemAtlas;
        using (var s = File.OpenRead(itemAtlasPath))
            itemAtlas = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);

        mItemAtlasTilePixels = itemAtlas.Width / UvHelper.TILE_COUNT;
        mItemAtlasAlpha = LoadAlphaFlipped(itemAtlas);

        ImageResult worldAtlas;
        using (var s = File.OpenRead(worldAtlasPath))
            worldAtlas = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);

        mWorldAtlasTilePixels = worldAtlas.Width / UvHelper.TILE_COUNT;
        mWorldAtlasAlpha = LoadAlphaFlipped(worldAtlas);

        StbImage.stbi_set_flip_vertically_on_load(1);
    }

    // Animation

    public void TriggerSwing() => mSwingRequested = true;

    public void Update(float deltaTime, float horizontalSpeed)
    {
        if (mSwingProgress >= 0f)
        {
            mSwingProgress += deltaTime * SWING_SPEED;

            if (mSwingProgress >= 1f)
                mSwingProgress = mSwingRequested ? 0f : -1f;
        }
        else if (mSwingRequested)
        {
            mSwingProgress = 0f;
        }

        mSwingRequested = false;

        if (mEquipProgress >= 0f)
        {
            mEquipProgress += deltaTime * EQUIP_SPEED;

            if (mEquipProgress >= 0.5f && mDisplayedStack != mPendingStack)
                mDisplayedStack = mPendingStack;

            if (mEquipProgress >= 1f)
                mEquipProgress = -1f;
        }

        if (horizontalSpeed > 0.1f)
            mBobPhase += deltaTime * BOB_SPEED;
    }

    private FrameAnim ComputeFrameAnim()
    {
        float bobY = MathF.Abs(MathF.Sin(mBobPhase)) * BOB_AMOUNT_Y;
        float bobX = MathF.Sin(mBobPhase) * BOB_AMOUNT_X;
        float bobTilt = MathF.Sin(mBobPhase) * BOB_TILT_DEG;
        float equipDip = mEquipProgress >= 0f ? MathF.Sin(mEquipProgress * MathF.PI) * EQUIP_AMOUNT : 0f;

        float swingY = 0f, swingZ = 0f;
        if (mSwingProgress >= 0f)
        {
            float t = mSwingProgress;

            if (t < 0.25f)
            {
                float u = t / 0.25f;
                swingY = 0.08f * MathF.Sin(u * MathF.PI * 0.5f);
            }
            else if (t < 0.55f)
            {
                float u = (t - 0.25f) / 0.30f;
                swingY = (0.08f + (-0.06f - 0.08f) * u);
                swingZ = -0.2f * MathF.Sin(u * MathF.PI);
            }
            else
            {
                float u = 1f - (t - 0.55f) / 0.45f;
                swingY = -0.06f * u;
            }
        }

        return new FrameAnim(bobX, bobY + equipDip, bobTilt, swingY, swingZ, equipDip);
    }

    // Rendering

    public void Render(Camera camera, ItemStack? heldStack)
    {
        Matrix4x4 proj =
            Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(ARM_FOV), camera.AspectRatio, 0.05f, 10f);

        GlContext.Gl.Clear(ClearBufferMask.DepthBufferBit);

        int bx = (int)MathF.Floor(camera.Position.X);
        int by = (int)MathF.Floor(camera.Position.Y);
        int bz = (int)MathF.Floor(camera.Position.Z);

        float skyLight = World.GetSkyLightGlobal(bx, by, bz) / (float)Chunk.MAX_LIGHT;
        float blockLight = World.GetBlockLightGlobal(bx, by, bz) / (float)Chunk.MAX_LIGHT;

        if (heldStack != mDisplayedStack && heldStack != mPendingStack)
        {
            mPendingStack = heldStack;
            mEquipProgress = 0f;
        }

        var anim = ComputeFrameAnim();

        if (mDisplayedStack.HasValue)
        {
            var stack = mDisplayedStack.Value;

            if (stack.IsBlock)
                RenderHeldBlock(stack.Block, proj, anim, skyLight, blockLight);
            else
                RenderHeldItem(stack, proj, anim, skyLight, blockLight);
        }
        else
        {
            RenderArm(proj, anim, skyLight, blockLight);
        }
    }

    private void RenderArm(Matrix4x4 proj, FrameAnim a, float skyLight, float blockLight)
    {
        Entity._shader?.Use();
        Entity._shader?.SetVector3("lightDir", Entity.LightDir);
        Entity._shader?.SetFloat("ambientStrength", Entity.AmbientStrength);
        Entity._shader?.SetFloat("sunlightLevel", Entity.SunlightLevel);
        Entity._shader?.SetFloat("skyLight", skyLight);
        Entity._shader?.SetFloat("blockLight", blockLight);
        Entity._shader?.SetFloat("uHitFlash", 0f);

        Matrix4x4 transform =
            Matrix4x4.CreateScale(ARM_SCALE)
            * Matrix4x4.CreateRotationY(float.DegreesToRadians(180f))
            * Matrix4x4.CreateRotationX(float.DegreesToRadians(-60f))
            * Matrix4x4.CreateRotationY(float.DegreesToRadians(10f))
            * Matrix4x4.CreateTranslation(0.4f + a.BobX, -0.45f + a.SwingY - a.BobY, -0.3f + a.SwingZ)
            * Matrix4x4.CreateRotationZ(float.DegreesToRadians(a.BobTilt));

        Entity._shader?.SetMatrix4("mvp", transform * proj);
        mArmModel.Texture.Use(TextureUnit.Texture0);
        GlContext.Gl.BindVertexArray(mArmModel.Vao);
        GlContext.Gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mArmModel.VertexCount);
    }

    private void RenderHeldBlock(BlockType blockType, Matrix4x4 proj, FrameAnim a,
        float skyLight, float blockLight)
    {
        var key = ItemStack.FromBlock(blockType);
        if (mCachedStack != key)
        {
            mCachedStack = key;
            mCachedMesh = BuildBlockMesh(BlockRegistry.Get(blockType));
        }

        var mesh = mCachedMesh!;

        if (mesh.Length == 0)
            return;

        var renderType = BlockRegistry.Get(blockType).RenderType;
        Matrix4x4 transform;

        if (renderType == RenderingType.Stair)
        {
            transform =
                Matrix4x4.CreateTranslation(-0.5f, -0.5f, -0.5f)
                * Matrix4x4.CreateRotationX(float.DegreesToRadians(180f))
                * Matrix4x4.CreateRotationY(float.DegreesToRadians(135f))
                * Matrix4x4.CreateRotationX(float.DegreesToRadians(-30f))
                * Matrix4x4.CreateScale(BLOCK_SCALE)
                * Matrix4x4.CreateTranslation(0.52f + a.BobX, -0.38f + a.SwingY - a.BobY, -0.75f + a.SwingZ)
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(a.BobTilt));
        }
        else if (renderType == RenderingType.Cross || renderType == RenderingType.Torch)
        {
            transform =
                Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0f)
                * Matrix4x4.CreateScale(ITEM_SCALE)
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(mDecoRotZ))
                * Matrix4x4.CreateRotationX(float.DegreesToRadians(mDecoRotX))
                * Matrix4x4.CreateRotationY(float.DegreesToRadians(mDecoRotY))
                * Matrix4x4.CreateTranslation(mDecoTransX + a.BobX, mDecoTransY + a.SwingY - a.BobY,
                    mDecoTransZ + a.SwingZ)
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(a.BobTilt));
        }
        else
        {
            transform =
                Matrix4x4.CreateTranslation(-0.5f, -0.5f, -0.5f)
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(mBlockRotZ))
                * Matrix4x4.CreateRotationY(float.DegreesToRadians(mBlockRotY))
                * Matrix4x4.CreateRotationX(float.DegreesToRadians(mBlockRotX))
                * Matrix4x4.CreateScale(BLOCK_SCALE)
                * Matrix4x4.CreateTranslation(mBlockTransX + a.BobX, mBlockTransY + a.SwingY - a.BobY,
                    mBlockTransZ + a.SwingZ)
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(a.BobTilt));
        }

        SetItemShader(transform * proj, Math.Max(skyLight, blockLight));
        GlContext.Gl.Disable(EnableCap.CullFace);
        mWorldTexture.Use(TextureUnit.Texture0);
        UploadAndDraw(mesh);
        GlContext.Gl.Enable(EnableCap.CullFace);
    }

    private void RenderHeldItem(ItemStack stack, Matrix4x4 proj, FrameAnim a,
        float skyLight, float blockLight)
    {
        if (mCachedStack != stack)
        {
            mCachedStack = stack;
            mCachedMesh = BuildThickSpriteMesh(ItemRegistry.Get(stack.Item).ItemCoords, mItemAtlasAlpha,
                mItemAtlasTilePixels);
        }

        var mesh = mCachedMesh!;

        if (mesh.Length == 0)
            return;

        bool isTool = ItemRegistry.Get(stack.Item).IsTool;
        bool isBow = stack.Item == ItemType.Bow;
        float rz = isTool ? mItemRotZ : mDecoRotZ;
        float rx = isTool ? mItemRotX : mDecoRotX;
        float ry = isTool ? mItemRotY : mDecoRotY;
        float tx = isBow ? mDecoTransX : isTool ? mItemTransX : mDecoTransX;
        float ty = isBow ? mDecoTransY : isTool ? mItemTransY : mDecoTransY;
        float tz = isBow ? mDecoTransZ : isTool ? mItemTransZ : mDecoTransZ;

        Matrix4x4 transform =
            Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0f)
            * Matrix4x4.CreateScale(ITEM_SCALE)
            * Matrix4x4.CreateRotationZ(float.DegreesToRadians(rz))
            * Matrix4x4.CreateRotationX(float.DegreesToRadians(rx))
            * Matrix4x4.CreateRotationY(float.DegreesToRadians(ry))
            * Matrix4x4.CreateTranslation(tx + a.BobX, ty + a.SwingY - a.BobY, tz + a.SwingZ)
            * Matrix4x4.CreateRotationZ(float.DegreesToRadians(a.BobTilt));

        SetItemShader(transform * proj, Math.Max(skyLight, blockLight));
        GlContext.Gl.Disable(EnableCap.CullFace);
        mItemTexture.Use(TextureUnit.Texture0);
        UploadAndDraw(mesh);
        GlContext.Gl.Enable(EnableCap.CullFace);
    }

    private void SetItemShader(Matrix4x4 mvp, float lightLevel)
    {
        mShader.Use();
        mShader.SetInt("tex", 0);
        mShader.SetFloat("lightLevel", lightLevel);
        mShader.SetMatrix4("mvp", mvp);
    }

    private void UploadAndDraw(float[] mesh)
    {
        var gl = GlContext.Gl;
        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, mesh, BufferUsageARB.DynamicDraw);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(mesh.Length / 6));
        gl.BindVertexArray(0);
    }

    // Mesh building

    private float[] BuildBlockMesh(Block block)
    {
        var min = block.BoundsMin;
        var max = block.BoundsMax;
        var verts = new List<float>();

        if (block.RenderType == RenderingType.Normal || block.RenderType == RenderingType.Slab)
        {
            AddBox(verts, min, max, block.TopTextureCoords, block.BottomTextureCoords, block.FrontTextureCoords,
                block.BackTextureCoords, block.RightTextureCoords, block.LeftTextureCoords);

            return verts.ToArray();
        }

        if (block.RenderType == RenderingType.Stair)
        {
            Vector3 mid = new(max.X, min.Y + 0.5f * (max.Y - min.Y), max.Z);
            AddBox(verts, min, mid, block.TopTextureCoords, block.BottomTextureCoords, block.FrontTextureCoords,
                block.BackTextureCoords, block.RightTextureCoords, block.LeftTextureCoords);

            AddBox(verts, new Vector3(min.X, mid.Y, min.Z + 0.5f * (max.Z - min.Z)), max, block.TopTextureCoords,
                block.BottomTextureCoords, block.FrontTextureCoords, block.BackTextureCoords, block.RightTextureCoords,
                block.LeftTextureCoords);

            for (int i = 0; i < verts.Count; i += 6)
            {
                verts[i + 1] = 1f - verts[i + 1];
                verts[i + 2] = 1f - verts[i + 2];
            }

            return verts.ToArray();
        }

        if (block.RenderType == RenderingType.Cross || block.RenderType == RenderingType.Torch)
            return BuildThickSpriteMesh(block.InventoryTextureCoords, mWorldAtlasAlpha, mWorldAtlasTilePixels);

        return BuildFlatSpriteMesh(block.InventoryTextureCoords);
    }

    private float[] BuildThickSpriteMesh(TextureCoords tileTex, byte[,] atlasAlpha, int tilePixels)
    {
        var verts = new List<float>();
        int tp = tilePixels;
        float ps = 1.0f / tp;
        float hd = THICK_DEPTH * 0.5f;
        float tps = (tileTex.BottomRight.X - tileTex.TopLeft.X) / tp;

        float uBase = tileTex.TopLeft.X;
        float vBase = tileTex.TopLeft.Y;

        for (int py = 0; py < tp; py++)
        {
            for (int px = 0; px < tp; px++)
            {
                int atlasCol = (int)(uBase / (1.0f / UvHelper.TILE_COUNT) * tp) + px;
                int atlasRow = (int)(vBase / (1.0f / UvHelper.TILE_COUNT) * tp) + py;

                if (atlasAlpha[atlasCol, atlasRow] < 10)
                    continue;

                float x0 = px * ps;
                float x1 = (px + 1) * ps;
                float y0 = py * ps;
                float y1 = (py + 1) * ps;
                float z0 = -hd;
                float z1 = hd;

                float uMid = uBase + (px + 0.5f) * tps;
                float vMid = vBase + (py + 0.5f) * tps;

                bool hasLeft = px > 0 && atlasAlpha[atlasCol - 1, atlasRow] >= 10;
                bool hasRight = px < tp - 1 && atlasAlpha[atlasCol + 1, atlasRow] >= 10;
                bool hasTop = py < tp - 1 && atlasAlpha[atlasCol, atlasRow + 1] >= 10;
                bool hasBottom = py > 0 && atlasAlpha[atlasCol, atlasRow - 1] >= 10;

                AddThickQuad(verts, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1),
                    new Vector3(x0, y1, z1), uMid, vMid, SHADE_FRONT);
                AddThickQuad(verts, new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0),
                    new Vector3(x1, y1, z0), uMid, vMid, SHADE_BACK);

                if (!hasLeft)
                    AddThickQuad(verts, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1),
                        new Vector3(x0, y1, z0), uMid, vMid, SHADE_LEFT);
                if (!hasRight)
                    AddThickQuad(verts, new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0),
                        new Vector3(x1, y1, z1), uMid, vMid, SHADE_RIGHT);
                if (!hasBottom)
                    AddThickQuad(verts, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0),
                        new Vector3(x0, y0, z0), uMid, vMid, SHADE_BOTTOM);
                if (!hasTop)
                    AddThickQuad(verts, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1),
                        new Vector3(x0, y1, z1), uMid, vMid, SHADE_TOP);
            }
        }

        return verts.ToArray();
    }

    private static float[] BuildFlatSpriteMesh(TextureCoords tex)
    {
        var verts = new List<float>();
        float u0 = tex.TopLeft.X, v0 = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1 = tex.BottomRight.Y;

        AddVertex(verts, new Vector3(0, 0, 0), u0, v0, 1f);
        AddVertex(verts, new Vector3(1, 0, 0), u1, v0, 1f);
        AddVertex(verts, new Vector3(1, 1, 0), u1, v1, 1f);
        AddVertex(verts, new Vector3(0, 0, 0), u0, v0, 1f);
        AddVertex(verts, new Vector3(1, 1, 0), u1, v1, 1f);
        AddVertex(verts, new Vector3(0, 1, 0), u0, v1, 1f);

        return verts.ToArray();
    }

    // Mesh helpers

    private static void AddBox(List<float> verts, Vector3 min, Vector3 max, TextureCoords top, TextureCoords bottom,
        TextureCoords front, TextureCoords back, TextureCoords right, TextureCoords left)
    {
        AddQuad(verts,
            new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z),
            top, SHADE_TOP);
        AddQuad(verts,
            new Vector3(min.X, min.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z), new Vector3(min.X, min.Y, min.Z),
            bottom, SHADE_BOTTOM);
        AddQuad(verts,
            new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
            front, SHADE_FRONT);
        AddQuad(verts,
            new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z), new Vector3(min.X, min.Y, min.Z),
            back, SHADE_BACK);
        AddQuad(verts,
            new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
            right, SHADE_RIGHT);
        AddQuad(verts,
            new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
            left, SHADE_LEFT);
    }

    private static void AddQuad(List<float> verts, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, TextureCoords tex,
        float shade)
    {
        float u0 = tex.TopLeft.X, v0t = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1t = tex.BottomRight.Y;

        AddVertex(verts, v0, u0, v0t, shade);
        AddVertex(verts, v1, u0, v1t, shade);
        AddVertex(verts, v2, u1, v1t, shade);
        AddVertex(verts, v0, u0, v0t, shade);
        AddVertex(verts, v2, u1, v1t, shade);
        AddVertex(verts, v3, u1, v0t, shade);
    }

    private static void AddThickQuad(List<float> verts, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float uMid,
        float vMid, float shade)
    {
        AddVertex(verts, v0, uMid, vMid, shade);
        AddVertex(verts, v1, uMid, vMid, shade);
        AddVertex(verts, v2, uMid, vMid, shade);
        AddVertex(verts, v0, uMid, vMid, shade);
        AddVertex(verts, v2, uMid, vMid, shade);
        AddVertex(verts, v3, uMid, vMid, shade);
    }

    private static void AddVertex(List<float> verts, Vector3 pos, float u, float v, float shade)
    {
        verts.Add(pos.X);
        verts.Add(pos.Y);
        verts.Add(pos.Z);
        verts.Add(u);
        verts.Add(v);
        verts.Add(shade);
    }

    private static byte[,] LoadAlphaFlipped(ImageResult img)
    {
        var alpha = new byte[img.Width, img.Height];
        for (int row = 0; row < img.Height; row++)
        for (int col = 0; col < img.Width; col++)
        {
            int glRow = img.Height - 1 - row;
            alpha[col, glRow] = img.Data[(row * img.Width + col) * 4 + 3];
        }

        return alpha;
    }
}

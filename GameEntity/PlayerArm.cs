// Main class that renders the player's arm. Right now it just uses the OBJ of the sheep's leg. Also, controls the swinging animation. | DA | 2/21/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class PlayerArm
{
    private const string ARM_MODEL = "Resources/Entities/Sheep/SheepLeg/SheepLeg.obj";
    private const string ARM_TEXTURE = "Resources/Entities/Sheep/SheepLeg/Leg.png";

    private const float ARM_FOV = 70f;
    private const float ARM_SCALE = 2f;

    private const float SWING_SPEED = 4f;

    // BOB_SPEED is divided by horizontalSpeed so the cycle rate stays constant regardless of walk speed.
    private const float BOB_SPEED = 12f;  // radians per second
    private const float BOB_AMOUNT_Y = 0.025f;
    private const float BOB_AMOUNT_X = 0.012f;
    private const float BOB_TILT_DEG = 3f;   // Z-rotation tilt per step

    private readonly EntityModel mArmModel;
    private float mSwingProgress = -1f;   // -1 = idle, 0-1 = swinging
    private float mBobPhase;
    private bool mSwingRequested;

    public PlayerArm()
    {
        Entity.InitShader();
        mArmModel = EntityModel.Load(ARM_MODEL, ARM_TEXTURE);
    }

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

        if (horizontalSpeed > 0.1f)
            mBobPhase += deltaTime * BOB_SPEED;
    }

    public void Render(Camera camera)
    {
        Matrix4 armProj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(ARM_FOV),
            camera.AspectRatio, 0.05f, 10f);

        GL.Clear(ClearBufferMask.DepthBufferBit);

        int bx = (int)MathF.Floor(camera.Position.X);
        int by = (int)MathF.Floor(camera.Position.Y);
        int bz = (int)MathF.Floor(camera.Position.Z);
        float skyLight = World.GetSkyLightGlobal(bx, by, bz) / (float)Chunk.MAX_LIGHT;
        float blockLight = World.GetBlockLightGlobal(bx, by, bz) / (float)Chunk.MAX_LIGHT;

        Entity._shader?.Use();
        Entity._shader?.SetVector3("lightDir", Entity.LightDir);
        Entity._shader?.SetFloat("ambientStrength", Entity.AmbientStrength);
        Entity._shader?.SetFloat("sunlightLevel", Entity.SunlightLevel);
        Entity._shader?.SetFloat("skyLight", skyLight);
        Entity._shader?.SetFloat("blockLight", blockLight);
        Entity._shader?.SetFloat("uHitFlash", 0f);

        // Tilt adds a slight rock synchronized with the X sway.
        float bobY = MathF.Abs(MathF.Sin(mBobPhase)) * BOB_AMOUNT_Y;
        float bobX = MathF.Sin(mBobPhase) * BOB_AMOUNT_X;
        float bobTilt = MathF.Sin(mBobPhase) * BOB_TILT_DEG;

        // wind-up (arm rises), strike (arc down + forward), return.
        float swingY = 0f, swingZ = 0f;
        if (mSwingProgress >= 0f)
        {
            float t = mSwingProgress;
            if (t < 0.25f)
            {
                // Wind-up
                float u = t / 0.25f;
                swingY = 0.08f * MathF.Sin(u * MathHelper.PiOver2);
            }
            else if (t < 0.55f)
            {
                // Strike
                float u = (t - 0.25f) / 0.30f;
                swingY = MathHelper.Lerp(0.08f, -0.06f, u);
                swingZ = -0.2f * MathF.Sin(u * MathF.PI);
            }
            else
            {
                // Return
                float u = 1f - (t - 0.55f) / 0.45f;
                swingY = -0.06f * u;
            }
        }

        Matrix4 armBase =
            Matrix4.CreateScale(ARM_SCALE)
            * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(180f))
            * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-60f))
            * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(10f))
            * Matrix4.CreateTranslation(0.4f + bobX, -0.45f + swingY - bobY, -0.3f + swingZ);

        Matrix4 armTransform = armBase * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(bobTilt));

        Matrix4 mvp = armTransform * armProj;

        Entity._shader?.SetMatrix4("mvp", mvp);
        mArmModel.Texture.Use(TextureUnit.Texture0);
        GL.BindVertexArray(mArmModel.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mArmModel.VertexCount);
    }
}

// Main Frustum culling file | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.Rendering;

public class Frustum
{
    private const int PLANE_COUNT = 6;

    private readonly Vector4[] mPlanes = new Vector4[PLANE_COUNT];

    public void Update(Matrix4 vp)
    {
        // Left, Right, Bottom, Top, Near, Far
        mPlanes[0] = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
        mPlanes[1] = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
        mPlanes[2] = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
        mPlanes[3] = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
        mPlanes[4] = new Vector4(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43);
        mPlanes[5] = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);

        for (int i = 0; i < PLANE_COUNT; i++)
        {
            float length = new Vector3(mPlanes[i].X, mPlanes[i].Y, mPlanes[i].Z).Length;
            mPlanes[i] /= length;
        }
    }

    // Is the cube visible in the Frustum box, if it is render it
    public bool IsBoxVisible(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < PLANE_COUNT; i++)
        {
            var plane = mPlanes[i];
            Vector3 positive = new(
                plane.X >= 0 ? max.X : min.X,
                plane.Y >= 0 ? max.Y : min.Y,
                plane.Z >= 0 ? max.Z : min.Z
            );

            if (Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), positive) + plane.W < 0)
                return false;
        }

        return true;
    }
}

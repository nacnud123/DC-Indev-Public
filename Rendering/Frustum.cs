// Main Frustum culling file | DA | 2/5/26


namespace VoxelEngine.Rendering;

/// <summary>
/// Represents the camera's view frustum as six planes, used to cull whole chunks (and other AABBs) that can't possibly be visible before spending time building/uploading/drawing their meshes. Extracts the planes directly from a combined view-projection matrix (Gribb/Hartmann method), which avoids having to separately reconstruct frustum corners from FOV/aspect/near/far.
/// </summary>
public class Frustum
{
    private const int PLANE_COUNT = 6;

    // Each plane is stored as (A, B, C, D) for the plane equation Ax + By + Cz + D = 0, where (A,B,C) is the outward-pointing normal and D is the signed distance term. Order: Left, Right, Bottom, Top, Near, Far.
    private readonly Vector4[] mPlanes = new Vector4[PLANE_COUNT];

    /// <summary>
    /// Recomputes the six frustum planes from a combined view * projection matrix. Call once per frame (or whenever the camera moves) before doing any <see cref="IsBoxVisible"/> queries.
    /// </summary>
    public void Update(Matrix4x4 vp)
    {
        // Gribb/Hartmann plane extraction: for a row-vector convention (System.Numerics.Matrix4x4 multiplies as v * M), each frustum plane is a linear combination of the rows of the view-projection matrix. Adding/subtracting the column-4 (w) row with the column-1/2/3 (x/y/z) rows yields the six clip-space boundary planes (w +/- x, w +/- y, w +/- z) mapped back into world space, without needing to know the FOV/aspect/near/far individually. Left, Right, Bottom, Top, Near, Far
        mPlanes[0] = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
        mPlanes[1] = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
        mPlanes[2] = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
        mPlanes[3] = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
        mPlanes[4] = new Vector4(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43);
        mPlanes[5] = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);

        // Normalize each plane so its normal (A,B,C) is unit length. This is required for the dot-product distance check in IsBoxVisible to give an actual signed distance rather than an arbitrarily-scaled value.
        for (int i = 0; i < PLANE_COUNT; i++)
        {
            float length = new Vector3(mPlanes[i].X, mPlanes[i].Y, mPlanes[i].Z).Length();
            mPlanes[i] /= length;
        }
    }

    /// <summary>
    /// Returns true if the axis-aligned box [min, max] intersects or lies inside the frustum, false if it is entirely outside (behind) at least one plane and can be safely skipped from rendering. This is a conservative "positive vertex" test: it can return true for boxes that are actually just outside a frustum corner (false positive), but never a false negative, which is the correct trade-off for culling (never skip something that should be drawn).
    /// </summary>
    public bool IsBoxVisible(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < PLANE_COUNT; i++)
        {
            var plane = mPlanes[i];
            // For each plane, pick the box corner that is furthest in the direction of the plane's normal (the "positive vertex" / p-vertex). If even this best-case corner is behind the plane, the whole box must be behind it too, so the box can be culled immediately.
            Vector3 positive = new(
                plane.X >= 0 ? max.X : min.X,
                plane.Y >= 0 ? max.Y : min.Y,
                plane.Z >= 0 ? max.Z : min.Z
            );

            // Signed distance from the plane; negative means fully behind (outside) this plane.
            if (Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), positive) + plane.W < 0)
                return false;
        }

        return true;
    }
}

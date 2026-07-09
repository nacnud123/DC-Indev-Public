// Main class that loads in the OBJs. | DA | 2/5/26

using System.Globalization;


namespace VoxelEngine.Utils;

/// <summary>
/// Parses a Wavefront .obj model file into a flat, GL-ready interleaved vertex buffer, split into per-material groups so each group can be drawn with its own texture (resolved separately via MtlLoader). Used for loading entity/item models (as opposed to voxel terrain, which is generated procedurally by ChunkMeshBuilder). OBJ is a plain-text format where each line is a directive: v  x y z        - a vertex position vt u v           - a texture coordinate vn x y z        - a vertex normal usemtl <name>    - switches the "current material" for subsequent faces f  a/b/c ...     - a face, listing vertex/texcoord/normal index triplets OBJ indices are 1-based and refer back into the v/vt/vn lists accumulated so far in the file (not per-face-local).
/// </summary>
public class ObjLoader
{
    // Each output vertex is interleaved as: position.xyz (3) + uv.xy (2) + normal.xyz (3) = 8 floats.
    public const int FLOATS_PER_VERTEX = 8; // pos(3) + uv(2) + norm(3)

    /// <summary>Flattened interleaved vertex buffer (pos+uv+normal per vertex) across all material groups combined, in group order.</summary>
    public float[] Vertices { get; private set; } = Array.Empty<float>();
    public int VertexCount { get; private set; }
    /// <summary>Per-material slice of the interleaved vertex data, keyed by material name (as declared via "usemtl"), so each material can be drawn with its own bound texture.</summary>
    public Dictionary<string, float[]> MaterialGroups { get; private set; } = new();

    /// <summary>
    /// Reads and parses the .obj file at <paramref name="path"/>, populating Vertices/VertexCount/MaterialGroups. Faces are triangulated via a fan (assumes convex polygons, which is standard for exported OBJ faces).
    /// </summary>
    public void Load(string path)
    {
        // Raw attribute pools, indexed by the 0-based (after -1 adjustment) indices used in face lines. OBJ accumulates these globally across the whole file, so a face can reference any v/vt/vn defined anywhere earlier in the file.
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var normals = new List<Vector3>();

        // Name of the material set by the most recent "usemtl" line; faces are bucketed into groupData under this key until it changes again.
        string? currentMaterial = null;
        var groupData = new Dictionary<string, List<float>>();
        // Scratch buffer reused per-face to avoid reallocating a list for every "f" line.
        var faceBuffer = new List<(int p, int t, int n)>(4);

        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v":
                    // Vertex position: "v x y z"
                    positions.Add(new Vector3(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    break;
                case "vt":
                    // Texture coordinate: "vt u v" (OBJ UVs are typically already in the model's own UV space, e.g. an atlas region baked in by the exporting tool).
                    texCoords.Add(new Vector2(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture)));
                    break;
                case "vn":
                    // Vertex normal: "vn x y z" (used for lighting if the model shader supports it).
                    normals.Add(new Vector3(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    break;
                case "usemtl":
                    // Switches which material subsequent faces belong to; lazily creates the group's vertex list on first use.
                    currentMaterial = parts[1];
                    if (!groupData.ContainsKey(currentMaterial))
                        groupData[currentMaterial] = new List<float>();
                    break;
                case "f":
                    // Face line; if no usemtl has been seen yet, target is null and ParseFace will just skip emitting vertices for it (no material to bucket into).
                    var target = currentMaterial != null ? groupData[currentMaterial] : null;
                    ParseFace(parts, positions, texCoords, normals, target, faceBuffer);
                    break;
            }
        }

        if (groupData.Count > 0)
        {
            // Concatenate all material groups into one combined buffer (Vertices) for convenience/whole-model use, while also keeping MaterialGroups so callers that want per-material draw calls (different textures) can use those directly.
            var all = new List<float>();
            foreach (var data in groupData.Values)
                all.AddRange(data);
            Vertices = all.ToArray();
            VertexCount = Vertices.Length / FLOATS_PER_VERTEX;

            foreach (var (mat, data) in groupData)
                MaterialGroups[mat] = data.ToArray();
        }
        else
        {
            Vertices = Array.Empty<float>();
            VertexCount = 0;
        }
    }

    /// <summary>
    /// Parses one "f ..." line into per-vertex (position, texcoord, normal) index triplets and triangulates the resulting polygon via a fan (v0, vi, vi+1), appending the expanded triangle vertices into <paramref name="target"/>. Handles OBJ's three face-vertex formats: "p", "p/t", "p/t/n", and "p//n".
    /// </summary>
    private static void ParseFace(
        string[] parts,
        List<Vector3> positions, List<Vector2> texCoords, List<Vector3> normals,
        List<float>? target,
        List<(int p, int t, int n)> faceBuffer)
    {
        faceBuffer.Clear();

        // parts[0] is "f"; each remaining token is one vertex reference for this face, e.g. "12/5/3" (pos/uv/normal), "12//3" (pos/normal, no uv), or "12" (position only).
        for (int i = 1; i < parts.Length; i++)
        {
            var idx = parts[i].Split('/');
            // OBJ indices are 1-based, so subtract 1 to get 0-based list indices.
            int p = int.Parse(idx[0]) - 1;
            // uv/normal indices are optional (e.g. "p//n" has an empty middle segment); default to index 0 when absent, matched up with the fallback in AddVertex.
            int t = idx.Length > 1 && idx[1] != "" ? int.Parse(idx[1]) - 1 : 0;
            int n = idx.Length > 2 ? int.Parse(idx[2]) - 1 : 0;
            faceBuffer.Add((p, t, n));
        }

        if (target == null)
            // No material context (no "usemtl" seen yet) - nothing to bucket these vertices into, so silently drop the face.
            return;

        // Triangle-fan triangulation: for an N-gon with vertices v0..vN-1, emit triangles (v0,v1,v2), (v0,v2,v3), ... (v0,vN-2,vN-1). This assumes the face is convex and planar, which holds for typical exported OBJ models (already triangulated or simple quads).
        for (int i = 1; i < faceBuffer.Count - 1; i++)
        {
            AddVertex(target, positions, texCoords, normals, faceBuffer[0]);
            AddVertex(target, positions, texCoords, normals, faceBuffer[i]);
            AddVertex(target, positions, texCoords, normals, faceBuffer[i + 1]);
        }
    }

    /// <summary>
    /// Resolves one (position, texcoord, normal) index triplet against the parsed attribute lists and appends the interleaved pos+uv+normal floats to <paramref name="data"/>, matching the FLOATS_PER_VERTEX layout.
    /// </summary>
    private static void AddVertex(List<float> data, List<Vector3> pos, List<Vector2> uv, List<Vector3> norm,
        (int p, int t, int n) v)
    {
        var p = pos[v.p];
        // Guard against out-of-range/absent uv or normal indices (e.g. a model with no vt lines at all) by falling back to a default value instead of throwing.
        var t = v.t < uv.Count ? uv[v.t] : Vector2.Zero;
        var n = v.n < norm.Count ? norm[v.n] : Vector3.UnitY;

        // Interleave as pos.xyz, uv.xy, normal.xyz - must match FLOATS_PER_VERTEX (8) and whatever vertex attribute layout the model shader expects.
        data.Add(p.X);
        data.Add(p.Y);
        data.Add(p.Z);
        data.Add(t.X);
        data.Add(t.Y);
        data.Add(n.X);
        data.Add(n.Y);
        data.Add(n.Z);
    }
}
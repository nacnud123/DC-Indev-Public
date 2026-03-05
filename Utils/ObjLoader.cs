// Main class that loads in the OBJs. | DA | 2/5/26

using System.Globalization;
using OpenTK.Mathematics;

namespace VoxelEngine.Utils;

public class ObjLoader
{
    public const int FLOATS_PER_VERTEX = 8; // pos(3) + uv(2) + norm(3)

    public float[] Vertices { get; private set; } = Array.Empty<float>();
    public int VertexCount { get; private set; }
    public Dictionary<string, float[]> MaterialGroups { get; private set; } = new();

    public void Load(string path)
    {
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var normals = new List<Vector3>();

        string? currentMaterial = null;
        var groupData = new Dictionary<string, List<float>>();
        var faceBuffer = new List<(int p, int t, int n)>(4);

        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vector3(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    break;
                case "vt":
                    texCoords.Add(new Vector2(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture)));
                    break;
                case "vn":
                    normals.Add(new Vector3(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    break;
                case "usemtl":
                    currentMaterial = parts[1];
                    if (!groupData.ContainsKey(currentMaterial))
                        groupData[currentMaterial] = new List<float>();
                    break;
                case "f":
                    var target = currentMaterial != null ? groupData[currentMaterial] : null;
                    ParseFace(parts, positions, texCoords, normals, target, faceBuffer);
                    break;
            }
        }

        if (groupData.Count > 0)
        {
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

    private static void ParseFace(
        string[] parts,
        List<Vector3> positions, List<Vector2> texCoords, List<Vector3> normals,
        List<float>? target,
        List<(int p, int t, int n)> faceBuffer)
    {
        faceBuffer.Clear();

        for (int i = 1; i < parts.Length; i++)
        {
            var idx = parts[i].Split('/');
            int p = int.Parse(idx[0]) - 1;
            int t = idx.Length > 1 && idx[1] != "" ? int.Parse(idx[1]) - 1 : 0;
            int n = idx.Length > 2 ? int.Parse(idx[2]) - 1 : 0;
            faceBuffer.Add((p, t, n));
        }

        if (target == null) 
            return;

        for (int i = 1; i < faceBuffer.Count - 1; i++)
        {
            AddVertex(target, positions, texCoords, normals, faceBuffer[0]);
            AddVertex(target, positions, texCoords, normals, faceBuffer[i]);
            AddVertex(target, positions, texCoords, normals, faceBuffer[i + 1]);
        }
    }

    private static void AddVertex(List<float> data, List<Vector3> pos, List<Vector2> uv, List<Vector3> norm,
        (int p, int t, int n) v)
    {
        var p = pos[v.p];
        var t = v.t < uv.Count ? uv[v.t] : Vector2.Zero;
        var n = v.n < norm.Count ? norm[v.n] : Vector3.UnitY;

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
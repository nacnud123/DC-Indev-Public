// Main class that loads in the OBJs. | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.Utils;

public class ObjLoader
{
    public float[] Vertices { get; private set; } = Array.Empty<float>();
    public int VertexCount { get; private set; }

    public void Load(string path)
    {
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var normals = new List<Vector3>();
        var vertexData = new List<float>();

        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vector3(float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
                    break;
                case "vt":
                    texCoords.Add(new Vector2(float.Parse(parts[1]), float.Parse(parts[2])));
                    break;
                case "vn":
                    normals.Add(new Vector3(float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
                    break;
                case "f":
                    ParseFace(parts, positions, texCoords, normals, vertexData);
                    break;
            }
        }

        Vertices = vertexData.ToArray();
        VertexCount = vertexData.Count / 8;
    }

    private static void ParseFace(string[] parts, List<Vector3> positions, List<Vector2> texCoords, List<Vector3> normals, List<float> vertexData)
    {
        var faceVerts = new List<(int p, int t, int n)>();

        for (int i = 1; i < parts.Length; i++)
        {
            var idx = parts[i].Split('/');
            int p = int.Parse(idx[0]) - 1;
            int t = idx.Length > 1 && idx[1] != "" ? int.Parse(idx[1]) - 1 : 0;
            int n = idx.Length > 2 ? int.Parse(idx[2]) - 1 : 0;
            faceVerts.Add((p, t, n));
        }

        for (int i = 1; i < faceVerts.Count - 1; i++)
        {
            AddVertex(vertexData, positions, texCoords, normals, faceVerts[0]);
            AddVertex(vertexData, positions, texCoords, normals, faceVerts[i]);
            AddVertex(vertexData, positions, texCoords, normals, faceVerts[i + 1]);
        }
    }

    private static void AddVertex(List<float> data, List<Vector3> pos, List<Vector2> uv, List<Vector3> norm, (int p, int t, int n) v)
    {
        var p = pos[v.p];
        var t = v.t < uv.Count ? uv[v.t] : Vector2.Zero;
        var n = v.n < norm.Count ? norm[v.n] : Vector3.UnitY;

        data.Add(p.X); data.Add(p.Y); data.Add(p.Z);
        data.Add(t.X); data.Add(t.Y);
        data.Add(n.X); data.Add(n.Y); data.Add(n.Z);
    }
}

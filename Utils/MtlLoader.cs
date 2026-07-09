// Parses a .mtl file and returns a map of material name → texture path. | DA | 2/28/26
using System.Globalization;

namespace VoxelEngine.Utils;

/// <summary>
/// Minimal parser for Wavefront .mtl (material library) files. A .mtl file defines one or more named materials referenced by an .obj file's "usemtl" directives. This loader only cares about the diffuse texture map (map_Kd) for each material - lighting properties like Ka (ambient), Kd (diffuse color), Ks (specular) and Ns (shininess) are ignored since this engine's entity/item models are unlit/texture-only.
/// </summary>
public static class MtlLoader
{
    /// <summary>
    /// Reads an .mtl file and returns a map of material name -> absolute/relative path to that material's diffuse texture (map_Kd), resolved relative to the directory the .mtl file lives in (since texture paths inside the file are typically relative, e.g. "textures/foo.png").
    /// </summary>
    public static Dictionary<string, string> Load(string mtlPath)
    {
        // Texture paths in the .mtl are relative to the .mtl file's own folder, not to the working directory, so resolve against mtlDir below.
        string mtlDir  = Path.GetDirectoryName(mtlPath) ?? "";
        var result     = new Dictionary<string, string>();
        // Tracks which material block we're currently inside, since map_Kd lines belong to whichever "newmtl <name>" preceded them.
        string? curMat = null;

        foreach (var line in File.ReadLines(mtlPath))
        {
            // MTL lines are whitespace-separated tokens; the first token is the directive (newmtl, map_Kd, Ka, Kd, Ks, Ns, etc.) and the rest are args.
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                continue;

            if (parts[0] == "newmtl")
                // Begins a new material definition block; subsequent lines (Ka/Kd/Ks/map_Kd/...) apply to this material until the next "newmtl".
                curMat = parts[1];
            else if (parts[0] == "map_Kd" && curMat != null)
                // map_Kd = diffuse color texture map. This is the only property this engine consumes; combine with mtlDir since the path is relative.
                result[curMat] = Path.Combine(mtlDir, parts[1]);
        }

        return result;
    }
}

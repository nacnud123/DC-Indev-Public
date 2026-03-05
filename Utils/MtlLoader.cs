// Parses a .mtl file and returns a map of material name → texture path. | DA | 2/28/26
using System.Globalization;

namespace VoxelEngine.Utils;

public static class MtlLoader
{
    public static Dictionary<string, string> Load(string mtlPath)
    {
        string mtlDir  = Path.GetDirectoryName(mtlPath) ?? "";
        var result     = new Dictionary<string, string>();
        string? curMat = null;

        foreach (var line in File.ReadLines(mtlPath))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 0) 
                continue;

            if (parts[0] == "newmtl")
                curMat = parts[1];
            else if (parts[0] == "map_Kd" && curMat != null)
                result[curMat] = Path.Combine(mtlDir, parts[1]);
        }

        return result;
    }
}

using System.Globalization;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Conversion;

public static class FaceGenRewriter
{
    public static List<AssetRename> BuildRenames(ModScanResult scan, ConversionPlan plan)
    {
        var renames = new List<AssetRename>();
        if (plan.RecordMappings.Count == 0) return renames;
        var byOldId = plan.RecordMappings
            .Where(m => m.RecordType.Contains("Npc", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(m => m.OldObjectId);
        if (byOldId.Count == 0) return renames;
        var pluginName = plan.SourcePluginFileName;
        var pluginStem = Path.GetFileNameWithoutExtension(pluginName);
        foreach (var pair in scan.Assets.FaceGenPairs)
        {
            var folderMatches = string.Equals(pair.PluginFolder, pluginName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.PluginFolder, pluginStem, StringComparison.OrdinalIgnoreCase);
            if (!folderMatches) continue;
            if (!uint.TryParse(pair.FormIdHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var oldId)) continue;
            if (!byOldId.TryGetValue(oldId, out var mapping)) continue;
            if (mapping.OldObjectId == mapping.NewObjectId) continue;
            var oldHex = pair.FormIdHex;
            var newHex = mapping.NewObjectId.ToString("X8");
            foreach (var path in pair.RelativePaths)
            {
                var replaced = ReplaceFormIdInPath(path, oldHex, newHex);
                if (replaced == path) continue;
                renames.Add(new AssetRename { OldRelativePath = path, NewRelativePath = replaced, Reason = "NPC FormID renumber" });
            }
        }
        return renames;
    }

    public static string ReplaceFormIdInPath(string relativePath, string oldHex, string newHex)
    {
        var directory = Path.GetDirectoryName(relativePath) ?? "";
        var fileName = Path.GetFileName(relativePath);
        var dot = fileName.IndexOf('.');
        if (dot <= 0) return relativePath;
        var stem = fileName[..dot];
        if (!string.Equals(stem, oldHex, StringComparison.OrdinalIgnoreCase)) return relativePath;
        var newFileName = newHex + fileName[dot..];
        return string.IsNullOrEmpty(directory) ? newFileName : Path.Combine(directory, newFileName);
    }
}

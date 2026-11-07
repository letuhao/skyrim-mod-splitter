using System.Text;
using SkyrimModSplitter.Core.Rewriting;
using SkyrimModSplitter.Core.Scanning;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace SkyrimModSplitter.Core.Dependents;

public static class DependentPatcher
{
    public static DependentPatchResult Patch(DependentScanResult scan, DependentPatchOptions options)
    {
        var result = new DependentPatchResult();
        if (!scan.Checked)
        {
            result.Warnings.Add("dependent scan was unchecked; nothing was patched");
            return result;
        }
        var lookup = options.Remap
            .GroupBy(r => (r.SourcePlugin.ToLowerInvariant(), r.OldId))
            .ToDictionary(g => g.Key, g => (g.Last().NewPlugin, g.Last().NewId));
        if (lookup.Count == 0)
        {
            result.Warnings.Add("no FormID remap entries supplied; nothing was patched");
            return result;
        }
        Directory.CreateDirectory(options.OutputFolder);
        foreach (var dependent in scan.Plugins)
        {
            try
            {
                var outputPath = Path.Combine(options.OutputFolder, "plugins", dependent.RelativePath);
                var map = new Dictionary<FormKey, FormKey>();
                foreach (var entry in options.Remap)
                {
                    if (!entry.SourcePlugin.Equals(Path.GetFileName(dependent.FullPath), StringComparison.OrdinalIgnoreCase)
                        && !MastersContains(dependent.FullPath, entry.SourcePlugin)) continue;
                    var sourceKey = new FormKey(ModKey.FromNameAndExtension(entry.SourcePlugin), entry.OldId);
                    map[sourceKey] = new FormKey(ModKey.FromNameAndExtension(entry.NewPlugin), entry.NewId);
                }
                if (map.Count == 0) continue;
                var mod = SkyrimMod.CreateFromBinary(dependent.FullPath, SkyrimRelease.SkyrimSE);
                mod.RemapLinks(map);
                foreach (var plugin in options.Remap.Select(r => r.NewPlugin).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var key = ModKey.FromNameAndExtension(plugin);
                    if (!mod.ModHeader.MasterReferences.Any(m => m.Master.Equals(key)))
                        mod.ModHeader.MasterReferences.Add(new MasterReference { Master = key });
                }
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                mod.WriteToBinary(outputPath);
                result.WrittenFiles.Add(outputPath);
            }
            catch (Exception ex)
            {
                result.Errors.Add("dependent plugin patch failed for " + dependent.RelativePath + ": " + ex.Message);
            }
        }
        foreach (var config in scan.Configs)
        {
            try
            {
                var outputPath = Path.Combine(options.OutputFolder, "configs", config.RelativePath);
                var document = TextFileCodec.Read(config.FullPath);
                var changed = false;
                var rewritten = RewriteRules.FormIdToken.Replace(document.Text, match =>
                {
                    if (!RewriteRules.TryParse(match, out var id, out var plugin)) return match.Value;
                    if (plugin == null) return match.Value;
                    if (!lookup.TryGetValue((plugin.ToLowerInvariant(), id), out var target)) return match.Value;
                    changed = true;
                    return "0x" + target.NewId.ToString("X") + "~" + target.NewPlugin;
                });
                if (!changed) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                TextFileCodec.Write(outputPath, document, rewritten);
                result.WrittenFiles.Add(outputPath);
            }
            catch (Exception ex)
            {
                result.Errors.Add("dependent config patch failed for " + config.RelativePath + ": " + ex.Message);
            }
        }
        return result;
    }

    private static bool MastersContains(string pluginPath, string masterName)
    {
        try
        {
            var raw = RawPluginScanner.Scan(pluginPath);
            return raw.Masters.Any(m => m.Equals(masterName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SkyrimModSplitter.Core.Tools;

namespace SkyrimModSplitter.Core.Scanning;

public sealed class ModFolderScanner
{
    private static readonly Regex FaceGenRegex = new(
        @"(?i)(?:^|[\\/])facegendata[\\/](facegeom|facetint)[\\/]([^\\/]+)[\\/]([0-9A-F]{8})\.(nif|dds)$",
        RegexOptions.Compiled);
    private static readonly HashSet<string> PluginExtensions = new(StringComparer.OrdinalIgnoreCase) { ".esp", ".esm", ".esl" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".bsa", ".ba2" };
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pex", ".psc" };
    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ini", ".json", ".txt", ".pas", ".xml", ".toml", ".yaml", ".yml" };
    private static readonly HashSet<string> FormIdConfigExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ini", ".json", ".txt", ".pas" };

    public ModScanResult Scan(ScanOptions options)
    {
        var root = Path.GetFullPath(options.ModFolder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("mod folder not found: " + root);
        var result = new ModScanResult { ModFolder = root };
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => new FileEntry(p, Path.GetRelativePath(root, p)))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bsArch = ToolLocator.FindBSArch(options.BSArchPath);
        result.Tools.Add(new ToolPathStatus { Name = "BSArch", Path = bsArch, Found = bsArch != null, Note = bsArch == null ? "archive listing disabled" : null });

        var pluginEntries = files.Where(f => PluginExtensions.Contains(Path.GetExtension(f.FullPath))).ToList();
        var archiveEntries = files.Where(f => ArchiveExtensions.Contains(Path.GetExtension(f.FullPath))).ToList();
        var dataDirs = new List<string>();
        foreach (var dir in options.DataDirectories)
        {
            if (Directory.Exists(dir)) dataDirs.Add(Path.GetFullPath(dir));
            else result.Warnings.Add(new WarningItem { Code = "DATA_DIR_NOT_FOUND", Severity = Severity.Warning, Message = "data directory not found and was ignored: " + dir });
        }
        dataDirs = dataDirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var mastersPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dataDirs.Count > 0)
        {
            foreach (var d in dataDirs)
                foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.TopDirectoryOnly))
                    mastersPresent.Add(Path.GetFileName(f));
        }

        foreach (var plugin in pluginEntries)
        {
            var raw = RawPluginScanner.Scan(plugin.FullPath);
            var scanned = new PluginScanner().Scan(plugin.FullPath, raw, plugin.RelativePath);
            if (dataDirs.Count > 0)
            {
                foreach (var master in scanned.Masters)
                    if (!mastersPresent.Contains(master)) scanned.MissingMasters.Add(master);
                if (scanned.MissingMasters.Count > 0)
                    scanned.Warnings.Add(new WarningItem
                    {
                        Code = "MISSING_MASTERS",
                        Severity = Severity.Error,
                        Message = "missing master files: " + string.Join(", ", scanned.MissingMasters),
                    });
            }
            else
            {
                scanned.Warnings.Add(new WarningItem
                {
                    Code = "MASTERS_UNCHECKED",
                    Severity = Severity.Info,
                    Message = "no --data-dir supplied; master presence was not verified",
                });
            }
            result.Plugins.Add(scanned);
        }

        foreach (var archive in archiveEntries)
        {
            var info = BSArchService.List(archive.FullPath, bsArch);
            info.RelativePath = archive.RelativePath;
            if (!info.Parsed && info.Error != null)
                result.Warnings.Add(new WarningItem { Code = "ARCHIVE_LIST_FAILED", Severity = Severity.Warning, Message = archive.RelativePath + ": " + info.Error });
            result.Archives.Add(info);
        }

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FullPath);
            if (ScriptExtensions.Contains(ext))
            {
                result.Scripts.Add(new ScriptInfo
                {
                    FileName = Path.GetFileName(file.FullPath),
                    RelativePath = file.RelativePath,
                    Kind = ext.TrimStart('.').ToLowerInvariant(),
                    SizeBytes = file.Length,
                });
            }
        }

        foreach (var file in files.Where(f => ConfigExtensions.Contains(Path.GetExtension(f.FullPath))))
        {
            var name = Path.GetFileName(file.FullPath);
            if (name.Equals("meta.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            if (FormIdConfigExtensions.Contains(Path.GetExtension(file.FullPath)))
                result.ConfigFiles.Add(FormIdTextScanner.Scan(file.RelativePath, file.FullPath));
        }

        BuildAssetInventory(files, result.Assets);
        result.ScannedFileExtensions = result.Assets.ExtensionCounts.Keys.ToList();

        var target = ResolveTarget(result.Plugins, options.TargetPlugin);
        if (!string.IsNullOrWhiteSpace(options.TargetPlugin) && target != null
            && !string.Equals(target.FileName, options.TargetPlugin, StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add(new WarningItem
            {
                Code = "TARGET_PLUGIN_NOT_FOUND",
                Severity = Severity.Error,
                Message = "requested plugin not found; fell back to largest plugin: " + options.TargetPlugin,
            });
        }
        if (target != null)
        {
            target.IsTarget = true;
            result.TargetPluginFileName = target.FileName;
        }
        else
        {
            result.Warnings.Add(new WarningItem { Code = "NO_PLUGIN", Severity = Severity.Error, Message = "no .esp/.esm/.esl plugin found in mod folder" });
        }

        foreach (var plugin in result.Plugins)
            foreach (var w in plugin.Warnings) result.Warnings.Add(w);

        if (result.Archives.Any(a => a.Error != null && !a.Parsed))
            result.Warnings.Add(new WarningItem { Code = "ARCHIVE_COVERAGE", Severity = Severity.Warning, Message = "one or more archives could not be listed; archive coverage is incomplete" });

        result.ModFolderHash = ComputeFolderHash(files, result.Plugins);
        return result;
    }

    private static PluginScanResult? ResolveTarget(List<PluginScanResult> plugins, string? requested)
    {
        if (plugins.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = plugins.FirstOrDefault(p => string.Equals(p.FileName, requested, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return plugins.OrderByDescending(p => p.RawRecordCount).ThenBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).First();
    }

    private static void BuildAssetInventory(List<FileEntry> files, AssetInventory assets)
    {
        var faceGen = new Dictionary<string, FaceGenAssetPair>(StringComparer.OrdinalIgnoreCase);
        assets.TotalFiles = files.Count;
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FullPath).ToLowerInvariant();
            assets.ExtensionCounts[ext] = assets.ExtensionCounts.TryGetValue(ext, out var c) ? c + 1 : 1;
            switch (ext)
            {
                case ".nif": assets.MeshCount++; break;
                case ".dds": assets.TextureCount++; break;
                case ".wav":
                case ".xwm":
                case ".fuz":
                case ".lip": assets.SoundCount++; break;
                case ".seq": assets.SeqCount++; assets.SeqFiles.Add(file.RelativePath); break;
                case ".swf": assets.InterfaceCount++; break;
            }
            if (ext is ".fuz" or ".xwm" or ".wav" or ".lip")
            {
                assets.VoiceCount++;
                if (assets.VoiceSamples.Count < 20) assets.VoiceSamples.Add(file.RelativePath);
            }
            var m = FaceGenRegex.Match(file.RelativePath.Replace('/', '\\'));
            if (m.Success)
            {
                var folder = m.Groups[2].Value;
                var id = m.Groups[3].Value.ToUpperInvariant();
                var kind = m.Groups[1].Value.ToLowerInvariant();
                var slot = folder + "|" + id;
                if (!faceGen.TryGetValue(slot, out var pair))
                {
                    pair = new FaceGenAssetPair { PluginFolder = folder, FormIdHex = id };
                    faceGen[slot] = pair;
                }
                if (kind == "facegeom") pair.HasMesh = true; else pair.HasTint = true;
                pair.RelativePaths.Add(file.RelativePath);
            }
        }
        assets.FaceGenPairs = faceGen.Values.OrderBy(p => p.PluginFolder, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.FormIdHex, StringComparer.Ordinal).ToList();
        assets.FaceGenPairCount = assets.FaceGenPairs.Count;
        assets.FaceGenMissingPairCount = assets.FaceGenPairs.Count(p => !p.HasMesh || !p.HasTint);
    }

    private static string ComputeFolderHash(List<FileEntry> files, List<PluginScanResult> plugins)
    {
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            using var sha = SHA256.Create();
            using var fs = File.Open(f.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            sb.Append(f.RelativePath).Append('|').Append(f.Length).Append('|')
              .Append(Convert.ToHexString(sha.ComputeHash(fs))).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private sealed record FileEntry(string FullPath, string RelativePath)
    {
        public long Length => new FileInfo(FullPath).Length;
    }
}

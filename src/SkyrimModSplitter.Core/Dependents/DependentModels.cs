using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Dependents;

public sealed class DependentScanOptions
{
    public string TargetPluginFileName { get; set; } = "";
    public string? SearchRoot { get; set; }
    public string? Mo2ProfileDir { get; set; }
}

public sealed class DependentFile
{
    public string FullPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Evidence { get; set; } = "";
}

public sealed class DependentScanResult
{
    public bool Checked { get; set; }
    public string? CheckRoot { get; set; }
    public int PluginsScanned { get; set; }
    public int ConfigsScanned { get; set; }
    public List<DependentFile> Plugins { get; set; } = new();
    public List<DependentFile> Configs { get; set; } = new();
    public WarningItem? UncheckedWarning { get; set; }
}

public sealed class IdRemapEntry
{
    public string SourcePlugin { get; set; } = "";
    public uint OldId { get; set; }
    public string NewPlugin { get; set; } = "";
    public uint NewId { get; set; }
}

public sealed class DependentPatchOptions
{
    public string OutputFolder { get; set; } = "";
    public List<IdRemapEntry> Remap { get; set; } = new();
    public bool Overwrite { get; set; }
}

public sealed class DependentPatchResult
{
    public List<string> WrittenFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public static class DependentScanner
{
    private static readonly HashSet<string> PluginExtensions = new(StringComparer.OrdinalIgnoreCase) { ".esp", ".esm", ".esl" };
    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ini", ".json", ".txt" };
    private const long MaxConfigBytes = 8L * 1024 * 1024;

    public static DependentScanResult Scan(DependentScanOptions options)
    {
        var result = new DependentScanResult();
        var root = ResolveRoot(options);
        if (root == null)
        {
            result.UncheckedWarning = new WarningItem
            {
                Code = "DEPENDENTS_UNCHECKED",
                Severity = Severity.Warning,
                Message = "no MO2 profile or dependent search root supplied; external safety was not checked",
            };
            return result;
        }
        result.Checked = true;
        result.CheckRoot = root;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(file);
            var relative = Path.GetRelativePath(root, file);
            if (PluginExtensions.Contains(ext))
            {
                result.PluginsScanned++;
                try
                {
                    var raw = RawPluginScanner.Scan(file);
                    if (raw.Masters.Any(m => m.Equals(options.TargetPluginFileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Plugins.Add(new DependentFile
                        {
                            FullPath = file,
                            RelativePath = relative,
                            Kind = "plugin",
                            Evidence = "masters [" + string.Join(", ", raw.Masters) + "]",
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.UncheckedWarning ??= new WarningItem { Code = "DEPENDENT_PLUGIN_UNREADABLE", Severity = Severity.Warning, Message = "could not scan " + relative + ": " + ex.Message };
                }
            }
            else if (ConfigExtensions.Contains(ext))
            {
                var info = new FileInfo(file);
                if (info.Length > MaxConfigBytes) continue;
                result.ConfigsScanned++;
                try
                {
                    var text = File.ReadAllText(file);
                    if (text.Contains(options.TargetPluginFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Configs.Add(new DependentFile
                        {
                            FullPath = file,
                            RelativePath = relative,
                            Kind = "config",
                            Evidence = "contains plugin filename " + options.TargetPluginFileName,
                        });
                    }
                }
                catch
                {
                    // unreadable config: counted as scanned only
                }
            }
        }
        return result;
    }

    private static string? ResolveRoot(DependentScanOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SearchRoot) && Directory.Exists(options.SearchRoot))
            return Path.GetFullPath(options.SearchRoot);
        if (!string.IsNullOrWhiteSpace(options.Mo2ProfileDir))
        {
            var profile = Path.GetFullPath(options.Mo2ProfileDir);
            // Accept either the profile folder or its parent (profiles/).
            var profileParent = Directory.GetParent(profile);
            var mo2Root = profileParent?.Parent;
            if (mo2Root != null)
            {
                var mods = Path.Combine(mo2Root.FullName, "mods");
                if (Directory.Exists(mods)) return mods;
            }
        }
        return null;
    }
}


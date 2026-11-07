using SkyrimModSplitter.Core.Conversion;

namespace SkyrimModSplitter.Core.Rewriting;

public static class CoverageScanner
{
    public static CoverageSummary Scan(string stagingRoot, ConversionPlan plan)
    {
        var summary = new CoverageSummary();
        var oldIds = plan.RecordMappings.Select(m => m.OldObjectId).ToHashSet();
        if (oldIds.Count == 0) return summary;
        foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            if (RewriteRules.IsMetadataFile(fileName)) continue;
            if (!RewriteRules.EditableExtensions.Contains(Path.GetExtension(file))) continue;
            summary.TextFilesScanned++;
            try
            {
                var document = TextFileCodec.Read(file);
                var lineNumber = 0;
                foreach (var line in document.Text.Split('\n'))
                {
                    lineNumber++;
                    foreach (System.Text.RegularExpressions.Match match in RewriteRules.FormIdToken.Matches(line))
                    {
                        if (!RewriteRules.TryParse(match, out var id, out var plugin)) continue;
                        if (!RewriteRules.IsSelfPlugin(plugin, plan.SourcePluginFileName)) continue;
                        if (!oldIds.Contains(id)) continue;
                        summary.StaleOldTokens++;
                        if (summary.Samples.Count < 30)
                            summary.Samples.Add(Path.GetRelativePath(stagingRoot, file) + ":" + lineNumber + " " + match.Value);
                    }
                }
            }
            catch
            {
                // unreadable file: counted as scanned only
            }
        }
        foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            var isReportOnly = ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".toml", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
                || RewriteRules.IsMetadataFile(fileName);
            if (!isReportOnly) continue;
            try
            {
                var text = TextFileCodec.Read(file).Text;
                foreach (System.Text.RegularExpressions.Match match in RewriteRules.FormIdToken.Matches(text))
                {
                    if (!RewriteRules.TryParse(match, out var id, out var plugin)) continue;
                    if (!RewriteRules.IsSelfPlugin(plugin, plan.SourcePluginFileName)) continue;
                    if (!oldIds.Contains(id)) continue;
                    summary.ReportOnlyTokens++;
                }
            }
            catch { }
        }
        return summary;
    }
}

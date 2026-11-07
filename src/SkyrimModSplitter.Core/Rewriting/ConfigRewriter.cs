using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Rewriting;

public static class ConfigRewriter
{
    public static ConfigRewriteSummary Rewrite(string stagingRoot, ConversionPlan plan)
    {
        var summary = new ConfigRewriteSummary();
        var map = plan.RecordMappings.ToDictionary(m => m.OldObjectId, m => m.NewObjectId);
        if (map.Count == 0) return summary;
        foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            if (RewriteRules.IsMetadataFile(fileName)) continue;
            var ext = Path.GetExtension(file);
            if (RewriteRules.ReportOnlyExtensions.Contains(ext))
            {
                var pasText = TextFileCodec.Read(file).Text;
                var mapped = CountMappedTokens(pasText, map, plan.SourcePluginFileName);
                if (mapped > 0)
                    summary.Warnings.Add(new WarningItem
                    {
                        Code = "XEDIT_SCRIPT_REPORT_ONLY",
                        Severity = Severity.Warning,
                        Message = "xEdit .pas script contains " + mapped + " mapped FormID token(s); not edited: " + Path.GetRelativePath(stagingRoot, file),
                    });
                continue;
            }
            if (!RewriteRules.EditableExtensions.Contains(ext)) continue;
            summary.FilesScanned++;
            try
            {
                var relative = Path.GetRelativePath(stagingRoot, file);
                var document = TextFileCodec.Read(file);
                var lineStarts = BuildLineStarts(document.Text);
                var edits = new List<ConfigEdit>();
                var replaced = RewriteRules.FormIdToken.Replace(document.Text, match =>
                {
                    if (!RewriteRules.TryParse(match, out var id, out var plugin)) return match.Value;
                    if (!RewriteRules.IsSelfPlugin(plugin, plan.SourcePluginFileName))
                    {
                        summary.ExternalTokens++;
                        return match.Value;
                    }
                    if (!map.TryGetValue(id, out var newId))
                    {
                        summary.UnresolvedTokens++;
                        return match.Value;
                    }
                    var newToken = "0x" + newId.ToString("X");
                    if (plugin != null) newToken += "~" + plugin;
                    edits.Add(new ConfigEdit
                    {
                        RelativePath = relative,
                        LineNumber = LineOf(lineStarts, match.Index),
                        OldToken = match.Value,
                        NewToken = newToken,
                        Confidence = plugin == null ? "bare-local" : "plugin-qualified",
                    });
                    return newToken;
                });
                if (edits.Count > 0 && replaced != document.Text)
                {
                    TextFileCodec.Write(file, document, replaced);
                    summary.FilesModified++;
                    summary.Edits.AddRange(edits);
                }
            }
            catch (Exception ex)
            {
                summary.Warnings.Add(new WarningItem { Code = "CONFIG_REWRITE_FAILED", Severity = Severity.Warning, Message = Path.GetFileName(file) + ": " + ex.Message });
            }
        }
        return summary;
    }

    private static int CountMappedTokens(string text, IReadOnlyDictionary<uint, uint> map, string sourcePluginFileName)
    {
        var count = 0;
        foreach (System.Text.RegularExpressions.Match match in RewriteRules.FormIdToken.Matches(text))
        {
            if (!RewriteRules.TryParse(match, out var id, out var plugin)) continue;
            if (RewriteRules.IsSelfPlugin(plugin, sourcePluginFileName) && map.ContainsKey(id)) count++;
        }
        return count;
    }

    private static List<int> BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    private static int LineOf(List<int> starts, int index)
    {
        var lo = 0;
        var hi = starts.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (starts[mid] <= index) lo = mid + 1; else hi = mid - 1;
        }
        return Math.Max(1, hi + 1);
    }
}

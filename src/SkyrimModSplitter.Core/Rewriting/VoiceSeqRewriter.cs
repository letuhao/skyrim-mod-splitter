using System.Globalization;
using System.Text.RegularExpressions;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Rewriting;

public static class VoiceSeqRewriter
{
    private static readonly Regex VoiceStem = new(@"^(?<hex>[0-9A-Fa-f]{8})(?<suffix>_[0-9]+)?$", RegexOptions.Compiled);
    private static readonly HashSet<string> VoiceExtensions = new(StringComparer.OrdinalIgnoreCase) { ".fuz", ".lip", ".xwm", ".wav" };

    public static (List<VoiceRename> Renames, SeqSummary Seq) Rewrite(string stagingRoot, ConversionPlan plan, List<WarningItem> warnings)
    {
        var renames = new List<VoiceRename>();
        var map = plan.RecordMappings.ToDictionary(m => m.OldObjectId, m => m.NewObjectId);
        foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!VoiceExtensions.Contains(Path.GetExtension(file))) continue;
            var stem = Path.GetFileNameWithoutExtension(file);
            var match = VoiceStem.Match(stem);
            if (!match.Success) continue;
            if (!uint.TryParse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) continue;
            if (!map.TryGetValue(id, out var newId) || newId == id) continue;
            var suffix = match.Groups["suffix"].Success ? match.Groups["suffix"].Value : "";
            var newFileName = newId.ToString("X8") + suffix + Path.GetExtension(file);
            var newPath = Path.Combine(Path.GetDirectoryName(file) ?? stagingRoot, newFileName);
            if (File.Exists(newPath))
            {
                warnings.Add(new WarningItem { Code = "VOICE_RENAME_COLLISION", Severity = Severity.Warning, Message = "voice rename target exists: " + newFileName });
                continue;
            }
            File.Move(file, newPath);
            renames.Add(new VoiceRename
            {
                OldRelativePath = Path.GetRelativePath(stagingRoot, file),
                NewRelativePath = Path.GetRelativePath(stagingRoot, newPath),
                Reason = "voice filename FormID renumber",
            });
        }

        var seq = new SeqSummary();
        var seqFiles = Directory.EnumerateFiles(stagingRoot, "*.seq", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        seq.SeqFiles = seqFiles.Count;
        foreach (var file in seqFiles) seq.Files.Add(Path.GetRelativePath(stagingRoot, file));
        seq.DialogueRenumbered = plan.RecordMappings.Any(m =>
            m.RecordType.Contains("DialogResponses", StringComparison.OrdinalIgnoreCase)
            || m.RecordType.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
            || m.RecordType.Contains("Quest", StringComparison.OrdinalIgnoreCase));
        if (seqFiles.Count > 0 && seq.DialogueRenumbered)
        {
            var warning = new WarningItem
            {
                Code = "SEQ_NOT_REGENERATED",
                Severity = Severity.Warning,
                Message = "dialogue/quest records were renumbered but " + seqFiles.Count + " SEQ file(s) were not regenerated; regenerate dialogue SEQ in the Creation Kit for affected quests, or verify dialogue",
            };
            seq.Warnings.Add(warning);
            warnings.Add(warning);
        }
        return (renames, seq);
    }
}

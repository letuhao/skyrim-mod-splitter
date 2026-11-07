using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SkyrimModSplitter.Core.Conversion;

public static class ConversionReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteManifest(ConversionPlan plan, ConversionResult result, string outputPluginPath, string path)
    {
        var manifest = new
        {
            plan = new
            {
                plan.SourceFolder,
                plan.SourcePluginFileName,
                plan.SourcePluginPath,
                plan.SourcePluginSha256,
                plan.SourceFolderHash,
                plan.OutputFolder,
                verdict = plan.Verdict.ToString(),
                plan.VerdictReason,
                plan.NeedsRenumber,
                plan.NeedsEslFlag,
                plan.IsSplitRequired,
                plan.IsBlocked,
                plan.OwnRecordCount,
                plan.OverrideRecordCount,
                plan.RawRecordCount,
                plan.SourceStatedRecordCount,
                plan.SourceMasters,
                plan.EslBudget,
                plan.FirstNewObjectId,
            },
            recordMappings = plan.RecordMappings.Select(m => new
            {
                m.RecordType,
                m.EditorId,
                oldObjectId = "0x" + m.OldObjectId.ToString("X"),
                newObjectId = "0x" + m.NewObjectId.ToString("X"),
                m.OldFormKey,
                m.NewFormKey,
            }),
            assetRenames = plan.AssetRenames,
            configRewrite = new { result.ConfigRewrite.FilesScanned, result.ConfigRewrite.FilesModified, result.ConfigRewrite.ExternalTokens, result.ConfigRewrite.UnresolvedTokens, result.ConfigRewrite.Edits, warnings = result.ConfigRewrite.Warnings },
            papyrus = new { result.Papyrus.PscScanned, result.Papyrus.PscModified, result.Papyrus.PexScanned, result.Papyrus.PexDecompiled, result.Papyrus.PexLiteralTokens, result.Papyrus.Scripts, warnings = result.Papyrus.Warnings },
            voiceRenames = result.VoiceRenames,
            seq = result.Seq,
            coverage = new { result.Coverage.TextFilesScanned, result.Coverage.ReportOnlyTokens, result.Coverage.StaleOldTokens, result.Coverage.Passed, result.Coverage.Samples },
            warnings = plan.Warnings.Concat(result.Warnings),
            validation = new { result.Validation.Passed, result.Validation.Checks, result.Validation.Failures },
            output = new { result.OutputPluginPath, result.OutputPluginSha256, pluginBytes = new FileInfo(outputPluginPath).Length },
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
    }

    public static void WriteReport(ConversionPlan plan, ConversionResult result, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Skyrim Mod Splitter — ESL Conversion Report");
        sb.AppendLine();
        sb.AppendLine("| field | value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| source folder | " + plan.SourceFolder + " |");
        sb.AppendLine("| source plugin | " + plan.SourcePluginFileName + " |");
        sb.AppendLine("| source SHA256 | " + plan.SourcePluginSha256 + " |");
        sb.AppendLine("| verdict | " + plan.Verdict + " |");
        sb.AppendLine("| reason | " + plan.VerdictReason + " |");
        sb.AppendLine("| own records | " + plan.OwnRecordCount + " |");
        sb.AppendLine("| overrides | " + plan.OverrideRecordCount + " |");
        sb.AppendLine("| renumber | " + plan.NeedsRenumber + " |");
        sb.AppendLine("| ESL flag needed | " + plan.NeedsEslFlag + " |");
        sb.AppendLine("| output plugin | " + result.OutputPluginPath + " |");
        sb.AppendLine("| output SHA256 | " + result.OutputPluginSha256 + " |");
        sb.AppendLine("| validation | " + (result.Validation.Passed ? "PASS" : "FAIL") + " |");
        sb.AppendLine();
        sb.AppendLine("## Validation checks");
        sb.AppendLine();
        foreach (var check in result.Validation.Checks) sb.AppendLine("- " + check);
        if (result.Validation.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Validation failures");
            sb.AppendLine();
            foreach (var failure in result.Validation.Failures) sb.AppendLine("- " + failure);
        }
        sb.AppendLine();
        sb.AppendLine("## Record mapping");
        sb.AppendLine();
        sb.AppendLine("| type | editorId | old ID | new ID |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var m in plan.RecordMappings)
            sb.AppendLine("| " + m.RecordType + " | " + (m.EditorId ?? "") + " | 0x" + m.OldObjectId.ToString("X") + " | 0x" + m.NewObjectId.ToString("X") + " |");
        sb.AppendLine();
        sb.AppendLine("## Asset renames");
        sb.AppendLine();
        if (plan.AssetRenames.Count == 0) sb.AppendLine("none");
        else
        {
            sb.AppendLine("| old path | new path | reason |");
            sb.AppendLine("|---|---|---|");
            foreach (var a in plan.AssetRenames) sb.AppendLine("| " + a.OldRelativePath + " | " + a.NewRelativePath + " | " + a.Reason + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## Content rewrite summary");
        sb.AppendLine();
        sb.AppendLine("- config files scanned: " + result.ConfigRewrite.FilesScanned + ", modified: " + result.ConfigRewrite.FilesModified + ", external tokens: " + result.ConfigRewrite.ExternalTokens + ", unresolved tokens: " + result.ConfigRewrite.UnresolvedTokens);
        foreach (var edit in result.ConfigRewrite.Edits.Take(500)) sb.AppendLine("  - " + edit.RelativePath + ":" + edit.LineNumber + " " + edit.OldToken + " -> " + edit.NewToken + " (" + edit.Confidence + ")");
        sb.AppendLine("- papyrus psc scanned: " + result.Papyrus.PscScanned + ", modified: " + result.Papyrus.PscModified + ", pex scanned/decompiled: " + result.Papyrus.PexScanned + "/" + result.Papyrus.PexDecompiled + ", pex literal tokens: " + result.Papyrus.PexLiteralTokens);
        sb.AppendLine("- voice files renamed: " + result.VoiceRenames.Count);
        sb.AppendLine("- SEQ files: " + result.Seq.SeqFiles + ", dialogue renumbered: " + result.Seq.DialogueRenumbered);
        sb.AppendLine("- masters preserved: " + result.Validation.Checks.FirstOrDefault(c => c.StartsWith("masters preserved")));
        sb.AppendLine("- coverage: text files scanned=" + result.Coverage.TextFilesScanned + ", report-only tokens=" + result.Coverage.ReportOnlyTokens + ", stale old tokens=" + result.Coverage.StaleOldTokens + ", passed=" + result.Coverage.Passed);
        foreach (var sample in result.Coverage.Samples.Take(20)) sb.AppendLine("  - stale: " + sample);
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        var warnings = plan.Warnings.Concat(result.Warnings).ToList();
        if (warnings.Count == 0) sb.AppendLine("none");
        else
        {
            sb.AppendLine("| severity | code | message |");
            sb.AppendLine("|---|---|---|");
            foreach (var w in warnings) sb.AppendLine("| " + w.Severity + " | " + w.Code + " | " + w.Message.Replace("|", "\\|") + " |");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}

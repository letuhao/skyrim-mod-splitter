using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SkyrimModSplitter.Core.Split;

public static class SplitReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteManifest(SplitPlan plan, SplitResult result, string path)
    {
        var manifest = new
        {
            plan = new
            {
                plan.SourceFolder,
                plan.SourcePluginFileName,
                plan.SourcePluginPath,
                plan.SourceSha256,
                plan.TotalOwnRecords,
                plan.EslBudget,
                plan.LoadOrder,
                parts = plan.Parts.Select(p => new { p.Index, p.FileName, p.OwnRecordCount, p.OverrideRecordCount, p.IsMaster, p.Masters }),
            },
            assignments = plan.Assignments.Select(a => new { a.RecordType, a.EditorId, oldObjectId = "0x" + a.OldObjectId.ToString("X"), newObjectId = "0x" + a.NewObjectId.ToString("X"), a.PartIndex }),
            result.PartPaths,
            result.PartSha256,
            warnings = plan.Warnings.Concat(result.Warnings),
            validation = new { result.Validation.Passed, result.Validation.Checks, result.Validation.Failures },
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
    }

    public static void WriteReport(SplitPlan plan, SplitResult result, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Skyrim Mod Splitter — Split Report");
        sb.AppendLine();
        sb.AppendLine("| field | value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| source plugin | " + plan.SourcePluginFileName + " |");
        sb.AppendLine("| source SHA256 | " + plan.SourceSha256 + " |");
        sb.AppendLine("| own records | " + plan.TotalOwnRecords + " |");
        sb.AppendLine("| ESL budget | " + plan.EslBudget + " |");
        sb.AppendLine("| load order | " + string.Join(" -> ", plan.LoadOrder) + " |");
        sb.AppendLine("| validation | " + (result.Validation.Passed ? "PASS" : "FAIL") + " |");
        sb.AppendLine();
        sb.AppendLine("## Parts");
        sb.AppendLine();
        sb.AppendLine("| part | file | own | master | SHA256 |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var part in plan.Parts)
            sb.AppendLine("| " + part.Index + " | " + part.FileName + " | " + part.OwnRecordCount + " | " + part.IsMaster + " | " + (result.PartSha256.TryGetValue(part.Index, out var sha) ? sha : "") + " |");
        sb.AppendLine();
        sb.AppendLine("## Validation");
        sb.AppendLine();
        foreach (var check in result.Validation.Checks) sb.AppendLine("- " + check);
        if (result.Validation.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Failures");
            sb.AppendLine();
            foreach (var failure in result.Validation.Failures) sb.AppendLine("- " + failure);
        }
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        var warnings = plan.Warnings.Concat(result.Warnings).ToList();
        if (warnings.Count == 0) sb.AppendLine("none");
        else foreach (var w in warnings) sb.AppendLine("- [" + w.Severity + "] " + w.Code + ": " + w.Message);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SkyrimModSplitter.Core.Reports;

public static class ScanReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteJson(Scanning.ModScanResult result, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
    }

    public static void WriteMarkdown(Scanning.ModScanResult result, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        sb.AppendLine("# Skyrim Mod Splitter — Scan Report");
        sb.AppendLine();
        sb.AppendLine("| field | value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| mod folder | " + result.ModFolder + " |");
        sb.AppendLine("| folder hash | " + result.ModFolderHash + " |");
        sb.AppendLine("| scanned at (UTC) | " + result.ScannedAtUtc.ToString("O") + " |");
        sb.AppendLine("| target plugin | " + (result.TargetPluginFileName ?? "<none>") + " |");
        sb.AppendLine("| plugins | " + result.Plugins.Count + " |");
        sb.AppendLine("| archives | " + result.Archives.Count + " |");
        sb.AppendLine("| scripts | " + result.Scripts.Count + " |");
        sb.AppendLine("| config files | " + result.ConfigFiles.Count + " |");
        sb.AppendLine("| warnings | " + result.Warnings.Count + " |");
        sb.AppendLine();
        sb.AppendLine("## Plugins");
        sb.AppendLine();
        sb.AppendLine("| plugin | records (raw) | records (Mutagen) | own | overrides | own <0x800 | max own ID | ESL now | renumber | split | VMAD scripts | links self/master/other/unresolved |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var p in result.Plugins)
        {
            sb.AppendLine("| " + p.FileName + " | " + p.RawRecordCount + " | " + p.MutagenRecordCount + " | " + p.OwnRecordCount + " | " + p.OverrideRecordCount + " | " + p.OwnIdsBelow0x800 + " | 0x" + p.MaxOwnFormId.ToString("X") + " | " + p.EslSafe + " | " + p.RenumberRequired + " | " + p.SplitRequired + " | " + p.VmadScripts.Count + " | " + p.Links.Self + "/" + p.Links.Master + "/" + p.Links.Other + "/" + p.Links.Unresolved + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## Archives");
        sb.AppendLine();
        sb.AppendLine("| archive | size | files | format | parsed | error |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var a in result.Archives)
            sb.AppendLine("| " + a.RelativePath + " | " + a.SizeBytes + " | " + a.FilesCount + " | " + (a.Format ?? "") + " | " + a.Parsed + " | " + (a.Error ?? "") + " |");
        sb.AppendLine();
        sb.AppendLine("## Scripts");
        sb.AppendLine();
        sb.AppendLine("pex=" + result.Scripts.Count(s => s.Kind == "pex") + ", psc=" + result.Scripts.Count(s => s.Kind == "psc"));
        sb.AppendLine();
        sb.AppendLine("## Config files (dry-run FormID scan)");
        sb.AppendLine();
        sb.AppendLine("| file | formId tokens | plugin-name tokens | status | error |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in result.ConfigFiles)
            sb.AppendLine("| " + c.RelativePath + " | " + c.FormIdTokenCount + " | " + c.PluginNameTokenCount + " | " + c.CoverageStatus + " | " + (c.Error ?? "") + " |");
        sb.AppendLine();
        sb.AppendLine("## Assets");
        sb.AppendLine();
        sb.AppendLine("- total files: " + result.Assets.TotalFiles);
        sb.AppendLine("- meshes: " + result.Assets.MeshCount + ", textures: " + result.Assets.TextureCount + ", sounds: " + result.Assets.SoundCount + ", voice: " + result.Assets.VoiceCount);
        sb.AppendLine("- SEQ: " + result.Assets.SeqCount + ", interface: " + result.Assets.InterfaceCount);
        sb.AppendLine("- FaceGen pairs: " + result.Assets.FaceGenPairCount + " (missing half: " + result.Assets.FaceGenMissingPairCount + ")");
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        if (result.Warnings.Count == 0) sb.AppendLine("none");
        else
        {
            sb.AppendLine("| severity | code | message | context |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var w in result.Warnings)
                sb.AppendLine("| " + w.Severity + " | " + w.Code + " | " + Escape(w.Message) + " | " + Escape(w.Context ?? "") + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## Tools");
        sb.AppendLine();
        sb.AppendLine("| tool | found | path | note |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var t in result.Tools)
            sb.AppendLine("| " + t.Name + " | " + t.Found + " | " + (t.Path ?? "") + " | " + (t.Note ?? "") + " |");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
}

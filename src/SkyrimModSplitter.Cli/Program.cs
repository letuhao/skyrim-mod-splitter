using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Reports;
using SkyrimModSplitter.Core.Scanning;
using SkyrimModSplitter.Core.Dependents;
using SkyrimModSplitter.Core.Split;

internal static class Program
{
    private const string Usage =
        "sms — Skyrim Mod Splitter CLI (read-only input; writes only to --out)\n" +
        "\n" +
        "Commands:\n" +
        "  sms scan  <modFolder> [--plugin <file.esp>] [--data-dir <dir>]... [--out <dir>] [--bsarch <exe>]\n" +
        "  sms plan  <modFolder> [--plugin <file.esp>] [--data-dir <dir>]... [--out <outputDir>] [--plan-out <dir>]\n" +
        "  sms apply <modFolder> [--plugin <file.esp>] [--data-dir <dir>]... [--out <outputDir>] [--overwrite] [--allow-non-esl]\n" +
        "  sms split <modFolder> [--plugin <file.esp>] [--out <outputDir>] [--budget <n>] [--plan-out <dir>] [--apply]\n" +
        "\nDependent options (scan/apply/split): --mo2-profile <profileDir> | --dependent-search <dir> [--patch-dependents]\n" +
        "\n" +
        "Exit codes: 0 = success, 1 = error, 2 = usage, 3 = blocked/split-required/validation-failed.\n";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class CommonArgs
    {
        public string Folder = "";
        public string? Plugin;
        public string? BSArch;
        public string? OutDir;
        public string? PlanOut;
        public List<string> DataDirs = new();
        public bool Overwrite;
        public bool AllowNonEsl;
        public int Budget = 2048;
        public bool ApplySplit;
        public string? Mo2Profile;
        public string? DependentSearch;
        public bool PatchDependents;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help") { Console.WriteLine(Usage); return args.Length == 0 ? 2 : 0; }
        var command = args[0].ToLowerInvariant();
        if (command is not ("scan" or "plan" or "apply" or "split")) return Fail("unknown command: " + args[0]);

        var common = ParseCommon(args);
        if (common == null) return 2;
        try
        {
            return command switch
            {
                "scan" => ScanCommand(common),
                "plan" => PlanCommand(common),
                "split" => SplitCommand(common),
                _ => ApplyCommand(common),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    private static CommonArgs? ParseCommon(string[] args)
    {
        var common = new CommonArgs();
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--plugin": if (++i >= args.Length) return FailNull("--plugin needs a value"); common.Plugin = args[i]; break;
                case "--data-dir": if (++i >= args.Length) return FailNull("--data-dir needs a value"); common.DataDirs.Add(args[i]); break;
                case "--out": if (++i >= args.Length) return FailNull("--out needs a value"); common.OutDir = args[i]; break;
                case "--plan-out": if (++i >= args.Length) return FailNull("--plan-out needs a value"); common.PlanOut = args[i]; break;
                case "--bsarch": if (++i >= args.Length) return FailNull("--bsarch needs a value"); common.BSArch = args[i]; break;
                case "--overwrite": common.Overwrite = true; break;
                case "--allow-non-esl": common.AllowNonEsl = true; break;
                case "--budget": if (++i >= args.Length || !int.TryParse(args[i], out var budget)) return FailNull("--budget needs an integer"); common.Budget = budget; break;
                case "--apply": common.ApplySplit = true; break;
                case "--mo2-profile": if (++i >= args.Length) return FailNull("--mo2-profile needs a value"); common.Mo2Profile = args[i]; break;
                case "--dependent-search": if (++i >= args.Length) return FailNull("--dependent-search needs a value"); common.DependentSearch = args[i]; break;
                case "--patch-dependents": common.PatchDependents = true; break;
                case "-h":
                case "--help": Console.WriteLine(Usage); return null;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal)) return FailNull("unknown option: " + args[i]);
                    if (common.Folder.Length > 0) return FailNull("unexpected extra argument: " + args[i]);
                    common.Folder = args[i];
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(common.Folder)) return FailNull("missing modFolder");
        return common;
    }

    private static ModScanResult Scan(CommonArgs common)
    {
        var scan = new ModFolderScanner().Scan(new ScanOptions
        {
            ModFolder = common.Folder,
            TargetPlugin = common.Plugin,
            DataDirectories = common.DataDirs,
            BSArchPath = common.BSArch,
        });
        return scan;
    }

    private static int ScanCommand(CommonArgs common)
    {
        var result = Scan(common);
        var outDir = common.OutDir ?? DefaultArtifactDir("scans", common.Folder);
        Directory.CreateDirectory(outDir);
        var jsonPath = Path.Combine(outDir, "scan.json");
        var mdPath = Path.Combine(outDir, "SCAN-REPORT.md");
        ScanReportWriter.WriteJson(result, jsonPath);
        ScanReportWriter.WriteMarkdown(result, mdPath);
        PrintScan(result);
        Console.WriteLine("report: " + mdPath);
        if (result.Warnings.Any(w => w.Severity == Severity.Error))
        {
            Console.Error.WriteLine("scan produced error-severity warnings; inspect the report");
            return 1;
        }
        return 0;
    }

    private static int PlanCommand(CommonArgs common)
    {
        var scan = Scan(common);
        if (scan.Warnings.Any(w => w.Severity == Severity.Error))
        {
            PrintScan(scan);
            Console.Error.WriteLine("scan produced error-severity warnings; refusing to plan");
            return 3;
        }
        var outputFolder = common.OutDir ?? DefaultConversionOutput(common.Folder);
        var plan = new ConversionPlanner().Plan(scan, new ConversionOptions { OutputFolder = outputFolder, Overwrite = common.Overwrite, AllowNonEsl = common.AllowNonEsl });
        var planDir = common.PlanOut ?? DefaultArtifactDir("plans", common.Folder);
        Directory.CreateDirectory(planDir);
        var planJson = Path.Combine(planDir, "plan.json");
        var planMd = Path.Combine(planDir, "PLAN.md");
        File.WriteAllText(planJson, JsonSerializer.Serialize(plan, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(planMd, BuildPlanMarkdown(plan), new UTF8Encoding(false));
        PrintPlan(plan);
        Console.WriteLine("plan: " + planJson);
        return plan.IsBlocked ? 3 : 0;
    }

    private static int ApplyCommand(CommonArgs common)
    {
        var scan = Scan(common);
        if (scan.Warnings.Any(w => w.Severity == Severity.Error))
        {
            PrintScan(scan);
            Console.Error.WriteLine("scan produced error-severity warnings; refusing to convert");
            return 3;
        }
        var outputFolder = common.OutDir ?? DefaultConversionOutput(common.Folder);
        var options = new ConversionOptions
        {
            OutputFolder = outputFolder,
            Overwrite = common.Overwrite,
            AllowNonEsl = common.AllowNonEsl,
        };
        var plan = new ConversionPlanner().Plan(scan, options);
        PrintPlan(plan);
        if (plan.IsBlocked || plan.Verdict == ConversionVerdict.SplitRequired || plan.Verdict == ConversionVerdict.AlreadyLight)
        {
            Console.Error.WriteLine("conversion not allowed; see plan reason above");
            return 3;
        }
        var result = new EslConverter().Execute(plan, options, new Progress<string>(m => Console.WriteLine("[" + m + "]")));
        if (!result.Success)
        {
            foreach (var e in result.Errors) Console.Error.WriteLine("error: " + e);
            return result.Validation.Failures.Count > 0 ? 3 : 1;
        }
        Console.WriteLine("converted: " + result.OutputPluginPath);
        Console.WriteLine("manifest: " + result.ManifestPath);
        Console.WriteLine("report: " + result.ReportPath);
        Console.WriteLine("validation: " + (result.Validation.Passed ? "PASS" : "FAIL"));
        var conversionRemap = plan.RecordMappings.Select(m => new IdRemapEntry
        {
            SourcePlugin = plan.SourcePluginFileName,
            OldId = m.OldObjectId,
            NewPlugin = plan.SourcePluginFileName,
            NewId = m.NewObjectId,
        }).ToList();
        HandleDependents(common, plan.SourcePluginFileName, conversionRemap, result.OutputFolder);
        return 0;
    }

    private static void HandleDependents(CommonArgs common, string targetPlugin, List<IdRemapEntry> remap, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(common.Mo2Profile) && string.IsNullOrWhiteSpace(common.DependentSearch))
        {
            Console.WriteLine("[dependents] no MO2 profile / dependent search root supplied; external safety unchecked");
            return;
        }
        var scanOptions = new DependentScanOptions
        {
            TargetPluginFileName = targetPlugin,
            Mo2ProfileDir = common.Mo2Profile,
            SearchRoot = common.DependentSearch,
        };
        var scan = DependentScanner.Scan(scanOptions);
        if (!scan.Checked)
        {
            Console.WriteLine("[dependents] " + (scan.UncheckedWarning?.Message ?? "unchecked"));
            return;
        }
        Console.WriteLine("[dependents] scanned plugins=" + scan.PluginsScanned + " configs=" + scan.ConfigsScanned
            + " dependentPlugins=" + scan.Plugins.Count + " dependentConfigs=" + scan.Configs.Count + " root=" + scan.CheckRoot);
        foreach (var p in scan.Plugins) Console.WriteLine("  dependent plugin: " + p.RelativePath + " (" + p.Evidence + ")");
        foreach (var c in scan.Configs) Console.WriteLine("  dependent config: " + c.RelativePath + " (" + c.Evidence + ")");
        if (!common.PatchDependents)
        {
            if (scan.Plugins.Count + scan.Configs.Count > 0)
                Console.WriteLine("[dependents] re-run with --patch-dependents to emit rewritten copies");
            return;
        }
        var patchOptions = new DependentPatchOptions
        {
            OutputFolder = Path.Combine(outputFolder, "dependents"),
            Remap = remap,
        };
        var patch = DependentPatcher.Patch(scan, patchOptions);
        Console.WriteLine("[dependents] patched files=" + patch.WrittenFiles.Count);
        foreach (var f in patch.WrittenFiles) Console.WriteLine("  written: " + f);
        foreach (var e in patch.Errors) Console.Error.WriteLine("  dependent patch error: " + e);
        foreach (var w in patch.Warnings) Console.WriteLine("  dependent patch warning: " + w);
        if (patch.WrittenFiles.Count > 0 || patch.Errors.Count > 0)
        {
            var report = new { targetPlugin, root = scan.CheckRoot, scannedPlugins = scan.PluginsScanned, scannedConfigs = scan.ConfigsScanned, dependentPlugins = scan.Plugins, dependentConfigs = scan.Configs, writtenFiles = patch.WrittenFiles, errors = patch.Errors, warnings = patch.Warnings };
            File.WriteAllText(Path.Combine(outputFolder, "dependents-manifest.json"), JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
        }
    }

    private static int SplitCommand(CommonArgs common)
    {
        var outputFolder = common.OutDir ?? Path.Combine(Environment.CurrentDirectory, "output", Sanitize(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(common.Folder)))) + "-split");
        var plan = new SplitPlanner().Plan(new SplitOptions
        {
            ModFolder = common.Folder,
            OutputFolder = outputFolder,
            Plugin = common.Plugin,
            EslBudget = common.Budget,
        });
        var planDir = common.PlanOut ?? DefaultArtifactDir("plans", common.Folder);
        Directory.CreateDirectory(planDir);
        File.WriteAllText(Path.Combine(planDir, "split-plan.json"), JsonSerializer.Serialize(plan, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(planDir, "SPLIT-PLAN.md"), BuildSplitMarkdown(plan), new UTF8Encoding(false));
        Console.WriteLine("split feasible: " + plan.Feasible + (plan.BlockedReason is null ? "" : " | reason: " + plan.BlockedReason));
        Console.WriteLine("own records: " + plan.TotalOwnRecords + " | budget: " + plan.EslBudget);
        foreach (var part in plan.Parts)
            Console.WriteLine("part " + part.Index + " file=" + part.FileName + " own=" + part.OwnRecordCount + " master=" + part.IsMaster + " masters=[" + string.Join(", ", part.Masters) + "]");
        Console.WriteLine("load order: " + string.Join(" -> ", plan.LoadOrder));
        foreach (var w in plan.Warnings) Console.WriteLine("[" + w.Severity + "] " + w.Code + ": " + w.Message);
        Console.WriteLine("plan: " + Path.Combine(planDir, "split-plan.json"));
        if (!plan.Feasible) return 3;
        if (!common.ApplySplit) return 0;

        Console.WriteLine("applying split...");
        var result = new SplitExecutor().Execute(plan, new SplitOptions
        {
            ModFolder = common.Folder,
            OutputFolder = outputFolder,
            Plugin = common.Plugin,
            EslBudget = common.Budget,
        }, new Progress<string>(m => Console.WriteLine("[" + m + "]")));
        Console.WriteLine("split success: " + result.Success);
        foreach (var part in plan.Parts)
            Console.WriteLine("part " + part.Index + " -> " + (result.PartPaths.TryGetValue(part.Index, out var path) ? path : "<none>"));
        Console.WriteLine("validation: " + (result.Validation.Passed ? "PASS" : "FAIL"));
        foreach (var failure in result.Validation.Failures) Console.Error.WriteLine("validation failure: " + failure);
        foreach (var error in result.Errors) Console.Error.WriteLine("error: " + error);
        if (result.Success) Console.WriteLine("report: " + result.ReportPath);
        if (result.Success)
        {
            var splitRemap = plan.Assignments.Select(a => new IdRemapEntry
            {
                SourcePlugin = plan.SourcePluginFileName,
                OldId = a.OldObjectId,
                NewPlugin = plan.Parts[a.PartIndex].FileName,
                NewId = a.NewObjectId,
            }).ToList();
            HandleDependents(common, plan.SourcePluginFileName, splitRemap, result.OutputFolder);
        }
        return result.Success ? 0 : 1;
    }

    private static string BuildSplitMarkdown(SplitPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Skyrim Mod Splitter — Split Plan");
        sb.AppendLine();
        sb.AppendLine("| field | value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| source plugin | " + plan.SourcePluginFileName + " |");
        sb.AppendLine("| source SHA256 | " + plan.SourceSha256 + " |");
        sb.AppendLine("| own records | " + plan.TotalOwnRecords + " |");
        sb.AppendLine("| ESL budget | " + plan.EslBudget + " |");
        sb.AppendLine("| feasible | " + plan.Feasible + " |");
        sb.AppendLine("| blocked reason | " + (plan.BlockedReason ?? "") + " |");
        sb.AppendLine("| load order | " + string.Join(" -> ", plan.LoadOrder) + " |");
        sb.AppendLine();
        sb.AppendLine("## Parts");
        sb.AppendLine();
        sb.AppendLine("| part | file | own records | master | masters |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var part in plan.Parts)
            sb.AppendLine("| " + part.Index + " | " + part.FileName + " | " + part.OwnRecordCount + " | " + part.IsMaster + " | " + string.Join(", ", part.Masters) + " |");
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        if (plan.Warnings.Count == 0) sb.AppendLine("none");
        else foreach (var w in plan.Warnings) sb.AppendLine("- [" + w.Severity + "] " + w.Code + ": " + w.Message);
        return sb.ToString();
    }

    private static void PrintScan(ModScanResult result)
    {
        Console.WriteLine("mod: " + result.ModFolder);
        Console.WriteLine("target plugin: " + (result.TargetPluginFileName ?? "<none>"));
        foreach (var p in result.Plugins)
            Console.WriteLine("plugin " + p.FileName + ": raw=" + p.RawRecordCount + " mutagen=" + p.MutagenRecordCount
                + " own=" + p.OwnRecordCount + " overrides=" + p.OverrideRecordCount
                + " below0x800=" + p.OwnIdsBelow0x800 + " eslNow=" + p.EslSafe
                + " renumber=" + p.RenumberRequired + " split=" + p.SplitRequired
                + " vmadScripts=" + p.VmadScripts.Count);
        Console.WriteLine("archives: " + result.Archives.Count + ", scripts: " + result.Scripts.Count
            + ", configs: " + result.ConfigFiles.Count + ", warnings: " + result.Warnings.Count);
    }

    private static void PrintPlan(ConversionPlan plan)
    {
        Console.WriteLine("plan: verdict=" + plan.Verdict + " reason=" + plan.VerdictReason);
        Console.WriteLine("records: own=" + plan.OwnRecordCount + " overrides=" + plan.OverrideRecordCount
            + " mappings=" + plan.RecordMappings.Count + " assetRenames=" + plan.AssetRenames.Count);
        Console.WriteLine("output: " + plan.OutputFolder);
    }

    private static string BuildPlanMarkdown(ConversionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Skyrim Mod Splitter — Conversion Plan");
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
        sb.AppendLine("| renumber required | " + plan.NeedsRenumber + " |");
        sb.AppendLine("| ESL flag required | " + plan.NeedsEslFlag + " |");
        sb.AppendLine("| output folder | " + plan.OutputFolder + " |");
        sb.AppendLine("| record mappings | " + plan.RecordMappings.Count + " |");
        sb.AppendLine("| asset renames | " + plan.AssetRenames.Count + " |");
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        if (plan.Warnings.Count == 0) sb.AppendLine("none");
        else foreach (var w in plan.Warnings) sb.AppendLine("- [" + w.Severity + "] " + w.Code + ": " + w.Message);
        return sb.ToString();
    }

    private static string DefaultArtifactDir(string kind, string modFolder)
        => Path.Combine(Environment.CurrentDirectory, "artifacts", kind, Sanitize(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(modFolder)))));

    private static string DefaultConversionOutput(string modFolder)
        => Path.Combine(Environment.CurrentDirectory, "output", Sanitize(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(modFolder)))) + "-ESL");

    private static string Sanitize(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static int Fail(string message) { Console.Error.WriteLine(message); Console.Error.WriteLine(Usage); return 2; }
    private static CommonArgs? FailNull(string message) { Console.Error.WriteLine(message); Console.Error.WriteLine(Usage); return null; }
}

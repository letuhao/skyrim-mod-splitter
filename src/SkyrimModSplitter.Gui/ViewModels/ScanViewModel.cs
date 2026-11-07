using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Gui.ViewModels;

public sealed class PluginRow
{
    public string FileName { get; init; } = "";
    public int RawRecords { get; init; }
    public int MutagenRecords { get; init; }
    public int Own { get; init; }
    public int Overrides { get; init; }
    public int Below0x800 { get; init; }
    public string MaxOwnId { get; init; } = "";
    public string EslNow { get; init; } = "";
    public string Renumber { get; init; } = "";
    public string Split { get; init; } = "";
    public int VmadScripts { get; init; }
}

public sealed class WarningRow
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class ScanViewModel : INotifyPropertyChanged
{
    private string _folder = "";
    private string _summary = "Pick a mod folder and press Scan.";
    private string _status = "Ready.";
    private string _lastOutputDir = "";
    private string _planText = "Scan a mod folder, then Plan ESL conversion.";
    private string _applyStatus = "";
    private bool _isBusy;
    private bool _canApply;
    private bool _canApplySplit;

    public ObservableCollection<PluginRow> Plugins { get; } = new();
    public ObservableCollection<WarningRow> Warnings { get; } = new();

    public string Folder { get => _folder; set { if (_folder == value) return; _folder = value; OnPropertyChanged(); } }
    public string Summary { get => _summary; private set { if (_summary == value) return; _summary = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { if (_status == value) return; _status = value; OnPropertyChanged(); } }
    public string LastOutputDir { get => _lastOutputDir; private set { if (_lastOutputDir == value) return; _lastOutputDir = value; OnPropertyChanged(); } }
    public string PlanText { get => _planText; private set { if (_planText == value) return; _planText = value; OnPropertyChanged(); } }
    public string ApplyStatus { get => _applyStatus; set { if (_applyStatus == value) return; _applyStatus = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; set { if (_isBusy == value) return; _isBusy = value; OnPropertyChanged(); } }
    public bool CanApply { get => _canApply; set { if (_canApply == value) return; _canApply = value; OnPropertyChanged(); } }
    public bool CanApplySplit { get => _canApplySplit; set { if (_canApplySplit == value) return; _canApplySplit = value; OnPropertyChanged(); } }

    public void Apply(ModScanResult result)
    {
        Plugins.Clear();
        Warnings.Clear();
        foreach (var p in result.Plugins)
        {
            Plugins.Add(new PluginRow
            {
                FileName = p.FileName,
                RawRecords = p.RawRecordCount,
                MutagenRecords = p.MutagenRecordCount,
                Own = p.OwnRecordCount,
                Overrides = p.OverrideRecordCount,
                Below0x800 = p.OwnIdsBelow0x800,
                MaxOwnId = "0x" + p.MaxOwnFormId.ToString("X"),
                EslNow = p.EslSafe ? "yes" : "no",
                Renumber = p.RenumberRequired ? "yes" : "no",
                Split = p.SplitRequired ? "yes" : "no",
                VmadScripts = p.VmadScripts.Count,
            });
        }
        foreach (var w in result.Warnings)
            Warnings.Add(new WarningRow { Severity = w.Severity.ToString(), Code = w.Code, Message = w.Message });

        var target = result.Plugins.FirstOrDefault(p => p.IsTarget);
        Summary = "Mod folder: " + result.ModFolder + Environment.NewLine +
                  "Target plugin: " + (result.TargetPluginFileName ?? "<none>") + Environment.NewLine +
                  "Plugins: " + result.Plugins.Count + " | Archives: " + result.Archives.Count +
                  " | Scripts: " + result.Scripts.Count + " | Configs: " + result.ConfigFiles.Count + Environment.NewLine +
                  "Target records: total=" + (target?.RawRecordCount ?? 0) +
                  ", own=" + (target?.OwnRecordCount ?? 0) +
                  ", overrides=" + (target?.OverrideRecordCount ?? 0) +
                  ", own<0x800=" + (target?.OwnIdsBelow0x800 ?? 0) + Environment.NewLine +
                  "ESL now: " + (target?.EslSafe ?? false) +
                  " | renumber required: " + (target?.RenumberRequired ?? false) +
                  " | split required: " + (target?.SplitRequired ?? false) + Environment.NewLine +
                  "FaceGen pairs: " + result.Assets.FaceGenPairCount +
                  " | SEQ: " + result.Assets.SeqCount +
                  " | warnings: " + result.Warnings.Count + Environment.NewLine +
                  "Folder hash: " + result.ModFolderHash;
        OnPropertyChanged(nameof(Summary));
        CanApply = false;
        ApplyStatus = "";
        PlanText = "Scan complete. Press Plan ESL conversion.";
    }

    public void SetPlan(ConversionPlan plan)
    {
        var lines = new List<string>
        {
            "Verdict: " + plan.Verdict,
            "Reason: " + plan.VerdictReason,
            "Source plugin: " + plan.SourcePluginFileName,
            "Own records: " + plan.OwnRecordCount + ", overrides: " + plan.OverrideRecordCount,
            "Renumber required: " + plan.NeedsRenumber + ", ESL flag required: " + plan.NeedsEslFlag,
            "Record mappings: " + plan.RecordMappings.Count + ", asset renames: " + plan.AssetRenames.Count,
            "Output folder: " + plan.OutputFolder,
            "",
            "Warnings:",
        };
        if (plan.Warnings.Count == 0) lines.Add("  none");
        else foreach (var w in plan.Warnings) lines.Add("  [" + w.Severity + "] " + w.Code + ": " + w.Message);
        PlanText = string.Join(Environment.NewLine, lines);
        CanApply = !plan.IsBlocked && plan.Verdict != ConversionVerdict.SplitRequired && plan.Verdict != ConversionVerdict.AlreadyLight;
        ApplyStatus = plan.Verdict == ConversionVerdict.AlreadyLight
            ? "Plugin is already ESL-flagged and ESL-safe; nothing to convert."
            : CanApply ? "Ready to apply." : "Conversion blocked by the plan; see warnings above.";
    }

    public void SetSplitPlan(Core.Split.SplitPlan plan)
    {
        var lines = new List<string>
        {
            "Split feasible: " + plan.Feasible,
            "Reason: " + (plan.BlockedReason ?? ""),
            "Own records: " + plan.TotalOwnRecords + " / budget " + plan.EslBudget,
            "Load order: " + string.Join(" -> ", plan.LoadOrder),
        };
        foreach (var part in plan.Parts)
            lines.Add("Part " + part.Index + ": " + part.FileName + " own=" + part.OwnRecordCount + " master=" + part.IsMaster + " masters=[" + string.Join(", ", part.Masters) + "]");
        foreach (var w in plan.Warnings) lines.Add("  [" + w.Severity + "] " + w.Code + ": " + w.Message);
        PlanText = string.Join(Environment.NewLine, lines);
        CanApplySplit = plan.Feasible;
        ApplyStatus = plan.Feasible ? "Split plan ready; press Apply split." : "Split blocked; see plan above.";
    }

    public void SetSplitResult(Core.Split.SplitResult result)
    {
        var lines = new List<string>
        {
            result.Success ? "Split SUCCEEDED." : "Split FAILED.",
            "Validation: " + (result.Validation.Passed ? "PASS" : "FAIL"),
        };
        foreach (var kv in result.PartPaths) lines.Add("Part " + kv.Key + ": " + kv.Value);
        foreach (var check in result.Validation.Checks) lines.Add("  check: " + check);
        foreach (var failure in result.Validation.Failures) lines.Add("  FAILURE: " + failure);
        foreach (var error in result.Errors) lines.Add("  ERROR: " + error);
        ApplyStatus = string.Join(Environment.NewLine, lines);
        if (result.Success) LastOutputDir = result.OutputFolder;
    }

    public void SetConversionResult(ConversionResult result)
    {
        var lines = new List<string>
        {
            result.Success ? "Conversion SUCCEEDED." : "Conversion FAILED.",
            "Output plugin: " + result.OutputPluginPath,
            "Validation: " + (result.Validation.Passed ? "PASS" : "FAIL"),
            "Coverage: stale old tokens=" + result.Coverage.StaleOldTokens + ", passed=" + result.Coverage.Passed,
            "Config edits=" + result.ConfigRewrite.Edits.Count + ", papyrus modified=" + result.Papyrus.PscModified + ", voice renames=" + result.VoiceRenames.Count + ", SEQ files=" + result.Seq.SeqFiles,
        };
        foreach (var c in result.Validation.Checks) lines.Add("  check: " + c);
        foreach (var f in result.Validation.Failures) lines.Add("  FAILURE: " + f);
        foreach (var e in result.Errors) lines.Add("  ERROR: " + e);
        ApplyStatus = string.Join(Environment.NewLine, lines);
        if (result.Success) LastOutputDir = result.OutputFolder;
    }

    public void SetOutputDir(string dir)
    {
        LastOutputDir = dir;
        Status = "Scan complete. Reports: " + dir;
    }

    public void AddConversionWarnings(IEnumerable<WarningItem> warnings)
    {
        foreach (var w in warnings)
            Warnings.Add(new WarningRow { Severity = w.Severity.ToString(), Code = w.Code, Message = "[conversion] " + w.Message });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

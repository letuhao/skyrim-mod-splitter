using SkyrimModSplitter.Core.Rewriting;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Conversion;

public enum ConversionVerdict
{
    AlreadyLight,
    EslReady,
    EslRenumber,
    SplitRequired,
    Blocked,
}

public sealed class ConversionOptions
{
    public string OutputFolder { get; set; } = "";
    public bool Overwrite { get; set; }
    public bool AllowNonEsl { get; set; }
    public bool DryRun { get; set; }
    public bool InPlace { get; set; }
}

public sealed class RecordIdMapping
{
    public string RecordType { get; set; } = "";
    public string? EditorId { get; set; }
    public uint OldObjectId { get; set; }
    public uint NewObjectId { get; set; }
    public string OldFormKey { get; set; } = "";
    public string NewFormKey { get; set; } = "";
}

public sealed class AssetRename
{
    public string OldRelativePath { get; set; } = "";
    public string NewRelativePath { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ConversionPlan
{
    public string SourceFolder { get; set; } = "";
    public string SourcePluginFileName { get; set; } = "";
    public string SourcePluginPath { get; set; } = "";
    public string SourcePluginSha256 { get; set; } = "";
    public string SourceFolderHash { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public ConversionVerdict Verdict { get; set; }
    public string VerdictReason { get; set; } = "";
    public bool NeedsRenumber { get; set; }
    public bool NeedsEslFlag { get; set; }
    public bool IsSplitRequired { get; set; }
    public bool IsBlocked { get; set; }
    public int OwnRecordCount { get; set; }
    public int OverrideRecordCount { get; set; }
    public int RawRecordCount { get; set; }
    public uint SourceStatedRecordCount { get; set; }
    public List<string> SourceMasters { get; set; } = new();
    public int EslBudget { get; set; } = 2048;
    public uint FirstNewObjectId { get; set; } = 0x800;
    public List<RecordIdMapping> RecordMappings { get; set; } = new();
    public List<AssetRename> AssetRenames { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
    public DateTimeOffset PlannedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ValidationSummary
{
    public bool Passed { get; set; }
    public List<string> Checks { get; set; } = new();
    public List<string> Failures { get; set; } = new();
}

public sealed class ConversionResult
{
    public bool Success { get; set; }
    public string OutputFolder { get; set; } = "";
    public string OutputPluginPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string OutputPluginSha256 { get; set; } = "";
    public ValidationSummary Validation { get; set; } = new();
    public ConfigRewriteSummary ConfigRewrite { get; set; } = new();
    public PapyrusSummary Papyrus { get; set; } = new();
    public List<VoiceRename> VoiceRenames { get; set; } = new();
    public SeqSummary Seq { get; set; } = new();
    public CoverageSummary Coverage { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
}

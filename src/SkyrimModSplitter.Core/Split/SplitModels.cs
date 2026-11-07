using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Split;

public sealed class SplitOptions
{
    public string ModFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string? Plugin { get; set; }
    public int EslBudget { get; set; } = 2048;
}

public sealed class SplitRecordAssignment
{
    public uint OldObjectId { get; set; }
    public string OldFormKey { get; set; } = "";
    public string RecordType { get; set; } = "";
    public string? EditorId { get; set; }
    public int PartIndex { get; set; }
    public uint NewObjectId { get; set; }
}

public sealed class SplitPartPlan
{
    public int Index { get; set; }
    public string FileName { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public bool KeepsOriginalName { get; set; }
    public bool IsMaster { get; set; }
    public int OwnRecordCount { get; set; }
    public int OverrideRecordCount { get; set; }
    public List<string> Masters { get; set; } = new();
    public List<string> UnitFormKeys { get; set; } = new();
}

public sealed class SplitPlan
{
    public string SourceFolder { get; set; } = "";
    public string SourcePluginPath { get; set; } = "";
    public string SourcePluginFileName { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public List<string> SourceMasters { get; set; } = new();
    public List<SplitPartPlan> Parts { get; set; } = new();
    public List<string> LoadOrder { get; set; } = new();
    public List<SplitRecordAssignment> Assignments { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
    public int TotalOwnRecords { get; set; }
    public int EslBudget { get; set; } = 2048;
    public bool Feasible { get; set; }
    public string? BlockedReason { get; set; }
    public DateTimeOffset PlannedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Rewriting;

public sealed class ConfigEdit
{
    public string RelativePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string OldToken { get; set; } = "";
    public string NewToken { get; set; } = "";
    public string Confidence { get; set; } = "";
}

public sealed class ConfigRewriteSummary
{
    public int FilesScanned { get; set; }
    public int FilesModified { get; set; }
    public int ExternalTokens { get; set; }
    public int UnresolvedTokens { get; set; }
    public List<ConfigEdit> Edits { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
}

public sealed class PapyrusScriptResult
{
    public string RelativePath { get; set; } = "";
    public string Mode { get; set; } = "";
    public int LiteralTokens { get; set; }
    public int RewrittenTokens { get; set; }
    public bool Compiled { get; set; }
    public string? CompileOutput { get; set; }
    public string? DeployedPexPath { get; set; }
}

public sealed class PapyrusSummary
{
    public int PscScanned { get; set; }
    public int PscModified { get; set; }
    public int PexScanned { get; set; }
    public int PexDecompiled { get; set; }
    public int PexLiteralTokens { get; set; }
    public List<PapyrusScriptResult> Scripts { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
}

public sealed class VoiceRename
{
    public string OldRelativePath { get; set; } = "";
    public string NewRelativePath { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class SeqSummary
{
    public int SeqFiles { get; set; }
    public bool DialogueRenumbered { get; set; }
    public List<string> Files { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
}

public sealed class CoverageSummary
{
    public int TextFilesScanned { get; set; }
    public int ReportOnlyTokens { get; set; }
    public int StaleOldTokens { get; set; }
    public int AmbiguousTokens { get; set; }
    public List<string> Samples { get; set; } = new();
    public bool Passed => StaleOldTokens == 0;
}

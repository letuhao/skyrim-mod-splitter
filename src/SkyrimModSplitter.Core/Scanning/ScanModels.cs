using System.Text.Json.Serialization;

namespace SkyrimModSplitter.Core.Scanning;

public enum Severity { Info, Warning, Error }

public sealed class ScanOptions
{
    public string ModFolder { get; set; } = "";
    public string? TargetPlugin { get; set; }
    public List<string> DataDirectories { get; set; } = new();
    public string? Mo2ProfilePath { get; set; }
    public string? BSArchPath { get; set; }
}

public sealed class WarningItem
{
    public string Code { get; set; } = "";
    public Severity Severity { get; set; } = Severity.Warning;
    public string Message { get; set; } = "";
    public string? Context { get; set; }
}

public sealed class ToolPathStatus
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public bool Found { get; set; }
    public string? Note { get; set; }
}

public sealed class ModScanResult
{
    public string ModFolder { get; set; } = "";
    public string ModFolderHash { get; set; } = "";
    public DateTimeOffset ScannedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? TargetPluginFileName { get; set; }
    public List<PluginScanResult> Plugins { get; set; } = new();
    public List<ArchiveInfo> Archives { get; set; } = new();
    public List<ScriptInfo> Scripts { get; set; } = new();
    public List<ConfigFileScan> ConfigFiles { get; set; } = new();
    public AssetInventory Assets { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
    public List<ToolPathStatus> Tools { get; set; } = new();
    public List<string> ScannedFileExtensions { get; set; } = new();
}

public sealed class PluginScanResult
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool IsEsl { get; set; }
    public bool IsEsm { get; set; }
    public bool IsLocalized { get; set; }
    public ushort HeaderVersion { get; set; }
    public float HedrVersion { get; set; }
    public uint NextFormId { get; set; }
    public List<string> Masters { get; set; } = new();
    public List<string> MissingMasters { get; set; } = new();
    public int RawRecordCount { get; set; }
    public uint RawStatedRecordCount { get; set; }
    public bool RawCountsMatchHeader { get; set; }
    public bool ParsedByMutagen { get; set; }
    public string? ParseError { get; set; }
    public int MutagenRecordCount { get; set; }
    public int OwnRecordCount { get; set; }
    public int OverrideRecordCount { get; set; }
    public int OwnIdsBelow0x800 { get; set; }
    public uint MinOwnFormId { get; set; }
    public uint MaxOwnFormId { get; set; }
    public int DuplicateOwnFormIds { get; set; }
    public bool EslSafe { get; set; }
    public bool RenumberRequired { get; set; }
    public bool SplitRequired { get; set; }
    public int EslHeadroom { get; set; }
    public SortedDictionary<string, int> RecordCountsBySignature { get; set; } = new(StringComparer.Ordinal);
    public LinkStatistics Links { get; set; } = new();
    public List<VmadScriptInfo> VmadScripts { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
    public bool IsTarget { get; set; }
}

public sealed class LinkStatistics
{
    public int Self { get; set; }
    public int Master { get; set; }
    public int Other { get; set; }
    public int Unresolved { get; set; }
    public int Total { get; set; }
}

public sealed class VmadScriptInfo
{
    public string RecordFormKey { get; set; } = "";
    public string RecordType { get; set; } = "";
    public string ScriptName { get; set; } = "";
    public int PropertyCount { get; set; }
}

public sealed class ArchiveInfo
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool Parsed { get; set; }
    public string? Format { get; set; }
    public string? Version { get; set; }
    public int FilesCount { get; set; }
    public string? ArchiveFlags { get; set; }
    public int ExitCode { get; set; }
    public string? Error { get; set; }
}

public sealed class ScriptInfo
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Kind { get; set; } = "";
    public long SizeBytes { get; set; }
}

public sealed class FormIdTokenSample
{
    public int LineNumber { get; set; }
    public string Token { get; set; } = "";
}

public sealed class ConfigPluginReference
{
    public int LineNumber { get; set; }
    public string Token { get; set; } = "";
}

public sealed class ConfigFileScan
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Format { get; set; } = "";
    public int FormIdTokenCount { get; set; }
    public int PluginNameTokenCount { get; set; }
    public List<FormIdTokenSample> FormIdSamples { get; set; } = new();
    public List<ConfigPluginReference> PluginNameSamples { get; set; } = new();
    public string CoverageStatus { get; set; } = "dry-run-unclassified";
    public string? Error { get; set; }
}

public sealed class FaceGenAssetPair
{
    public string PluginFolder { get; set; } = "";
    public string FormIdHex { get; set; } = "";
    public bool HasMesh { get; set; }
    public bool HasTint { get; set; }
    public List<string> RelativePaths { get; set; } = new();
}

public sealed class AssetInventory
{
    public int TotalFiles { get; set; }
    public int MeshCount { get; set; }
    public int TextureCount { get; set; }
    public int SoundCount { get; set; }
    public int VoiceCount { get; set; }
    public int SeqCount { get; set; }
    public int InterfaceCount { get; set; }
    public int FaceGenPairCount { get; set; }
    public int FaceGenMissingPairCount { get; set; }
    public List<FaceGenAssetPair> FaceGenPairs { get; set; } = new();
    public List<string> SeqFiles { get; set; } = new();
    public List<string> VoiceSamples { get; set; } = new();
    public SortedDictionary<string, int> ExtensionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RawPluginInfo
{
    public string File { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public uint FlagsRaw { get; set; }
    public bool IsEsl => (FlagsRaw & 0x200) != 0;
    public bool IsEsm => (FlagsRaw & 0x1) != 0;
    public bool IsLocalized => (FlagsRaw & 0x80) != 0;
    public ushort HeaderVersion { get; set; }
    public float HedrVersion { get; set; }
    public uint NumRecordsStated { get; set; }
    public uint NextFormId { get; set; }
    public List<string> Masters { get; set; } = new();
    public List<string> HeaderSubrecords { get; set; } = new();
    public SortedDictionary<string, int> RecordCounts { get; set; } = new(StringComparer.Ordinal);
    public int TotalRecords { get; set; }
    public uint MinRecordFormId { get; set; } = uint.MaxValue;
    public uint MaxRecordFormId { get; set; }
    public bool CountsMatchHeader => TotalRecords == NumRecordsStated;
    public List<string> Errors { get; set; } = new();
}

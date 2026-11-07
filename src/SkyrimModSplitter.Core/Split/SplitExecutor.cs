using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Split;

public sealed class SplitValidation
{
    public bool Passed { get; set; }
    public List<string> Checks { get; set; } = new();
    public List<string> Failures { get; set; } = new();
}

public sealed class SplitResult
{
    public bool Success { get; set; }
    public string OutputFolder { get; set; } = "";
    public Dictionary<int, string> PartPaths { get; set; } = new();
    public Dictionary<int, string> PartSha256 { get; set; } = new();
    public string ManifestPath { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public SplitValidation Validation { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<WarningItem> Warnings { get; set; } = new();
}

public sealed class SplitExecutor
{
    private static readonly BinaryWriteParameters WriteParameters = new()
    {
        ModKey = ModKeyOption.NoCheck,
        MastersListContent = MastersListContentOption.NoCheck,
        MastersListOrdering = MastersListOrderingOption.NoCheck,
        RecordCount = RecordCountOption.Iterate,
        NextFormID = NextFormIDOption.NoCheck,
    };

    public SplitResult Execute(SplitPlan plan, SplitOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new SplitResult { OutputFolder = options.OutputFolder };
        string? staging = null;
        try
        {
            if (!plan.Feasible)
            {
                result.Errors.Add("split plan is not feasible: " + (plan.BlockedReason ?? "unknown reason"));
                return result;
            }
            if (plan.Parts.Count != 2)
            {
                result.Errors.Add("only two-part splits are implemented");
                return result;
            }
            var sourceFolder = Path.GetFullPath(plan.SourceFolder);
            var outputFolder = Path.GetFullPath(options.OutputFolder);
            if (IsSameOrInside(sourceFolder, outputFolder) || IsSameOrInside(outputFolder, sourceFolder))
            {
                result.Errors.Add("output folder must not be the source folder, inside it, or an ancestor of it");
                return result;
            }
            if (!File.Exists(plan.SourcePluginPath))
            {
                result.Errors.Add("source plugin missing: " + plan.SourcePluginPath);
                return result;
            }
            if (!string.Equals(HashFile(plan.SourcePluginPath), plan.SourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("source plugin changed since planning; re-run split");
                return result;
            }

            var partOriginal = plan.Parts[0];
            var partMaster = plan.Parts[1];
            var originalKey = ModKey.FromNameAndExtension(partOriginal.FileName);
            var masterKey = ModKey.FromNameAndExtension(partMaster.FileName);
            var sourceKey = ModKey.FromNameAndExtension(plan.SourcePluginFileName);

            staging = outputFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                      + ".staging-" + Guid.NewGuid().ToString("N")[..8];
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            var assignments = plan.Assignments;
            var finalKeys = assignments.ToDictionary(
                a => new FormKey(sourceKey, a.OldObjectId),
                a => new FormKey(a.PartIndex == 0 ? originalKey : masterKey, a.NewObjectId));
            var masterAssignments = assignments.Where(a => a.PartIndex == 1).ToList();
            var originalAssignments = assignments.Where(a => a.PartIndex == 0).ToList();

            progress?.Report("Building new master part " + partMaster.FileName);
            var masterPath = Path.Combine(staging, partMaster.FileName);
            var masterMod = new SkyrimMod(masterKey, SkyrimRelease.SkyrimSE);
            {
                var source = SkyrimMod.CreateFromBinary(plan.SourcePluginPath, SkyrimRelease.SkyrimSE);
                CopyHeader(source, masterMod, isMaster: true);
                foreach (var master in partMaster.Masters)
                    AddMaster(masterMod, ModKey.FromNameAndExtension(master));
                var ownById = source.EnumerateMajorRecords()
                    .Where(r => r.FormKey.ModKey.Equals(source.ModKey))
                    .ToDictionary(r => r.FormKey.ID);
                foreach (var assignment in masterAssignments)
                {
                    if (!ownById.TryGetValue(assignment.OldObjectId, out var record))
                        throw new InvalidOperationException("source record missing for master assignment 0x" + assignment.OldObjectId.ToString("X"));
                    var duplicate = record.Duplicate(new FormKey(masterKey, assignment.NewObjectId));
                    AddRecord(masterMod, duplicate);
                }
                masterMod.RemapLinks(finalKeys);
                masterMod.ModHeader.Stats.NextFormID = (uint)(0x800 + masterAssignments.Count);
                masterMod.ModHeader.Stats.NumRecords = (uint)masterMod.EnumerateMajorRecords().Count();
                masterMod.WriteToBinary(masterPath, WriteParameters);
            }
            result.PartPaths[partMaster.Index] = masterPath;
            result.PartSha256[partMaster.Index] = HashFile(masterPath);

            progress?.Report("Building dependent part " + partOriginal.FileName);
            var originalPath = Path.Combine(staging, partOriginal.FileName);
            {
                var source = SkyrimMod.CreateFromBinary(plan.SourcePluginPath, SkyrimRelease.SkyrimSE);
                var keepIds = originalAssignments.Select(a => a.OldObjectId).ToHashSet();
                var removed = RemoveOtherRecords(source, keepIds);
                var remainingOwn = source.EnumerateMajorRecords()
                    .Where(r => r.FormKey.ModKey.Equals(source.ModKey))
                    .ToList();
                var remainingIds = remainingOwn.Select(r => r.FormKey.ID).ToHashSet();
                if (!remainingIds.SetEquals(keepIds))
                {
                    var extra = remainingIds.Except(keepIds).Take(10).Select(id => "0x" + id.ToString("X"));
                    var missing = keepIds.Except(remainingIds).Take(10).Select(id => "0x" + id.ToString("X"));
                    throw new InvalidOperationException("original part record removal mismatch; extra=[" + string.Join(",", extra) + "] missing=[" + string.Join(",", missing) + "]");
                }
                var byId = remainingOwn.ToDictionary(r => r.FormKey.ID);
                var localMap = new Dictionary<FormKey, FormKey>();
                foreach (var assignment in originalAssignments)
                {
                    if (!byId.TryGetValue(assignment.OldObjectId, out var record))
                        throw new InvalidOperationException("original record missing for assignment 0x" + assignment.OldObjectId.ToString("X"));
                    var newKey = new FormKey(originalKey, assignment.NewObjectId);
                    FormKeyMutator.SetFormKey(record, newKey);
                    localMap[new FormKey(sourceKey, assignment.OldObjectId)] = newKey;
                }
                var remap = new Dictionary<FormKey, FormKey>(localMap);
                foreach (var kv in finalKeys)
                {
                    if (kv.Key.ModKey.Equals(sourceKey) && !remap.ContainsKey(kv.Key)) remap[kv.Key] = kv.Value;
                }
                source.RemapLinks(remap);
                AddMaster(source, masterKey);
                source.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.Small;
                source.ModHeader.Flags &= ~SkyrimModHeader.HeaderFlag.Master;
                source.ModHeader.Stats.NextFormID = (uint)(0x800 + originalAssignments.Count);
                source.ModHeader.Stats.NumRecords = (uint)source.EnumerateMajorRecords().Count();
                source.WriteToBinary(originalPath, WriteParameters);
                result.Warnings.Add(new WarningItem { Code = "SPLIT_RECORDS_REMOVED", Severity = Severity.Info, Message = removed + " top-level records removed from the original part" });
            }
            result.PartPaths[partOriginal.Index] = originalPath;
            result.PartSha256[partOriginal.Index] = HashFile(originalPath);

            progress?.Report("Copying loose assets");
            CopyLooseFiles(sourceFolder, staging, plan.SourcePluginFileName, result.Warnings);

            progress?.Report("Validating split output");
            result.Validation = SplitValidator.Validate(plan, originalPath, masterPath, originalKey, masterKey);

            progress?.Report("Writing split manifest/report");
            var manifestPath = Path.Combine(staging, "split-manifest.json");
            var reportPath = Path.Combine(staging, "SPLIT-REPORT.md");
            SplitReportWriter.WriteManifest(plan, result, manifestPath);
            SplitReportWriter.WriteReport(plan, result, reportPath);
            if (!result.Validation.Passed)
            {
                result.Errors.Add("split validation failed; output was not published");
                return result;
            }

            if (Directory.Exists(outputFolder))
            {
                if (!options.OutputFolder.Equals(outputFolder, StringComparison.OrdinalIgnoreCase) && Directory.Exists(outputFolder)) { }
                var backup = outputFolder + ".old-" + Guid.NewGuid().ToString("N")[..6];
                Directory.Move(outputFolder, backup);
                try { Directory.Move(staging, outputFolder); staging = null; Directory.Delete(backup, true); }
                catch { if (Directory.Exists(outputFolder)) Directory.Delete(outputFolder, true); Directory.Move(backup, outputFolder); throw; }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputFolder) ?? ".");
                Directory.Move(staging, outputFolder);
                staging = null;
            }

            foreach (var part in plan.Parts)
            {
                var final = Path.Combine(outputFolder, part.FileName);
                result.PartPaths[part.Index] = final;
                result.PartSha256[part.Index] = HashFile(final);
            }
            result.ManifestPath = Path.Combine(outputFolder, "split-manifest.json");
            result.ReportPath = Path.Combine(outputFolder, "SPLIT-REPORT.md");
            result.Success = true;
            progress?.Report("Split done: " + outputFolder);
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Errors.Add("split cancelled");
            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.GetType().Name + ": " + ex.Message);
            return result;
        }
        finally
        {
            if (staging != null && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); } catch { }
            }
        }
    }

    private static void CopyHeader(SkyrimMod source, SkyrimMod target, bool isMaster)
    {
        target.ModHeader.Flags = SkyrimModHeader.HeaderFlag.Small
            | (isMaster ? SkyrimModHeader.HeaderFlag.Master : 0)
            | (source.ModHeader.Flags & ~(SkyrimModHeader.HeaderFlag.Master | SkyrimModHeader.HeaderFlag.Small));
        target.ModHeader.FormVersion = source.ModHeader.FormVersion;
        target.ModHeader.Version = source.ModHeader.Version;
        target.ModHeader.Stats.Version = source.ModHeader.Stats.Version;
        target.ModHeader.Author = source.ModHeader.Author;
        target.ModHeader.Description = source.ModHeader.Description;
        if (source.ModHeader.INTV is { } intv) target.ModHeader.INTV = intv;
        if (source.ModHeader.INCC is { } incc) target.ModHeader.INCC = incc;
    }

    private static void AddMaster(SkyrimMod mod, ModKey master)
    {
        if (mod.ModHeader.MasterReferences.Any(m => m.Master.Equals(master))) return;
        mod.ModHeader.MasterReferences.Add(new MasterReference { Master = master });
    }

    private static void AddRecord(SkyrimMod mod, IMajorRecordGetter record)
    {
        foreach (var prop in typeof(ISkyrimMod).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.PropertyType.IsGenericType) continue;
            if (prop.GetValue(mod) is not IEnumerable group) continue;
            var groupType = group.GetType();
            var add = groupType.GetMethods()
                .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(record));
            if (add == null) continue;
            add.Invoke(group, new object[] { record });
            return;
        }
        throw new InvalidOperationException("no group found for split record type " + record.GetType().Name);
    }

    private static int RemoveOtherRecords(SkyrimMod mod, HashSet<uint> keepIds)
    {
        var removed = 0;
        foreach (var prop in typeof(ISkyrimMod).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.PropertyType.IsGenericType) continue;
            if (prop.GetValue(mod) is not IEnumerable group) continue;
            var groupType = group.GetType();
            var remove = groupType.GetMethod("Remove", new[] { typeof(FormKey) });
            if (remove == null) continue;
            var toRemove = new List<FormKey>();
            foreach (var item in group)
            {
                var record = Extract(item);
                if (record == null) continue;
                if (!record.FormKey.ModKey.Equals(mod.ModKey)) continue;
                if (!keepIds.Contains(record.FormKey.ID)) toRemove.Add(record.FormKey);
            }
            foreach (var formKey in toRemove)
            {
                remove.Invoke(group, new object[] { formKey });
                removed++;
            }
        }
        return removed;
    }

    private static IMajorRecordGetter? Extract(object? item)
    {
        if (item is IMajorRecordGetter record) return record;
        return item?.GetType().GetProperty("Value")?.GetValue(item) as IMajorRecordGetter;
    }

    private static void CopyLooseFiles(string sourceFolder, string staging, string pluginFileName, List<WarningItem> warnings)
    {
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, file);
            if (relative.Equals(pluginFileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.EndsWith(".esp", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new WarningItem { Code = "EXTRA_PLUGIN_COPIED", Severity = Severity.Warning, Message = "additional plugin copied unchanged: " + relative });
            }
            var destination = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
        if (Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Any(f => f.Contains("facegendata", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(new WarningItem { Code = "SPLIT_FACEGEN_NOT_REMAPPED", Severity = Severity.Warning, Message = "FaceGen assets are copied unchanged; NPC FormIDs split across parts may need asset renames in a later pass" });
        }
    }

    private static bool IsSameOrInside(string parent, string child)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        return string.Equals(p, c, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}

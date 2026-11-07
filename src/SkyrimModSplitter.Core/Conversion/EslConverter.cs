using System.Security.Cryptography;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Reports;
using SkyrimModSplitter.Core.Rewriting;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Conversion;

public sealed class EslConverter
{
    public ConversionResult Execute(
        ConversionPlan plan,
        ConversionOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ConversionResult { OutputFolder = options.OutputFolder };
        string? staging = null;
        try
        {
            if (plan.IsBlocked || plan.Verdict is ConversionVerdict.SplitRequired)
            {
                result.Errors.Add("conversion is not allowed: " + plan.VerdictReason);
                return result;
            }
            if (plan.Verdict == ConversionVerdict.AlreadyLight)
            {
                result.Errors.Add("plugin is already ESL-flagged and ESL-safe; no conversion is needed");
                return result;
            }
            if (options.InPlace)
            {
                result.Errors.Add("in-place mode is not implemented in this slice");
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
                result.Errors.Add("source plugin not found: " + plan.SourcePluginPath);
                return result;
            }
            var sourceHash = HashFile(plan.SourcePluginPath);
            if (!string.Equals(sourceHash, plan.SourcePluginSha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("source plugin changed after planning; re-run the scan/plan");
                return result;
            }

            staging = outputFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                      + ".staging-" + Guid.NewGuid().ToString("N")[..8];
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            var stagingPlugin = Path.Combine(staging, plan.SourcePluginFileName);

            progress?.Report("Loading source plugin");
            var mod = SkyrimMod.CreateFromBinary(plan.SourcePluginPath, SkyrimRelease.SkyrimSE);
            var records = mod.EnumerateMajorRecords().ToList();
            cancellationToken.ThrowIfCancellationRequested();

            if (plan.NeedsRenumber)
            {
                progress?.Report("Renumbering " + plan.RecordMappings.Count + " own records");
                var mutable = records
                    .Where(r => r.FormKey.ModKey.Equals(mod.ModKey))
                    .ToDictionary(r => r.FormKey);
                var map = new Dictionary<FormKey, FormKey>();
                foreach (var mapping in plan.RecordMappings)
                {
                    var oldKey = new FormKey(mod.ModKey, mapping.OldObjectId);
                    var newKey = new FormKey(mod.ModKey, mapping.NewObjectId);
                    map[oldKey] = newKey;
                    if (mutable.TryGetValue(oldKey, out var record))
                        FormKeyMutator.SetFormKey(record, newKey);
                    else
                        result.Warnings.Add(new WarningItem { Code = "MAPPING_RECORD_MISSING", Severity = Severity.Error, Message = "record not found while renumbering: " + oldKey });
                }
                progress?.Report("Remapping internal FormLinks");
                mod.RemapLinks(map);
            }

            if (plan.NeedsEslFlag)
            {
                mod.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.Small;
            }

            var maxOwnId = records
                .Where(r => r.FormKey.ModKey.Equals(mod.ModKey))
                .Select(r => r.FormKey.ID)
                .DefaultIfEmpty(0u)
                .Max();
            if (maxOwnId > 0)
                ModHeaderMutator.SetNextFormId(mod, maxOwnId + 1);

            progress?.Report("Writing converted plugin (master-preserving)");
            var writeParameters = new BinaryWriteParameters
            {
                ModKey = ModKeyOption.NoCheck,
                MastersListContent = MastersListContentOption.NoCheck,
                MastersListOrdering = MastersListOrderingOption.NoCheck,
                RecordCount = RecordCountOption.NoCheck,
                NextFormID = NextFormIDOption.NoCheck,
            };
            mod.WriteToBinary(stagingPlugin, writeParameters);

            progress?.Report("Copying loose assets");
            CopyLooseFiles(sourceFolder, staging, plan.SourcePluginFileName, plan.SourcePluginPath, result.Warnings);

            progress?.Report("Applying FaceGen renames");
            ApplyAssetRenames(staging, plan.AssetRenames, result.Warnings);

            progress?.Report("Rewriting config FormIDs");
            result.ConfigRewrite = ConfigRewriter.Rewrite(staging, plan);
            result.Warnings.AddRange(result.ConfigRewrite.Warnings);

            progress?.Report("Rewriting Papyrus scripts");
            result.Papyrus = PapyrusRewriter.Rewrite(staging, plan);
            result.Warnings.AddRange(result.Papyrus.Warnings);

            progress?.Report("Renaming voice assets / checking SEQ");
            var voiceSeq = VoiceSeqRewriter.Rewrite(staging, plan, result.Warnings);
            result.VoiceRenames = voiceSeq.Renames;
            result.Seq = voiceSeq.Seq;

            progress?.Report("Coverage scan");
            result.Coverage = CoverageScanner.Scan(staging, plan);
            if (result.Coverage.ReportOnlyTokens > 0)
                result.Warnings.Add(new WarningItem { Code = "COVERAGE_REPORT_ONLY_TOKENS", Severity = Severity.Warning, Message = result.Coverage.ReportOnlyTokens + " mapped FormID token(s) live in non-editable text (.xml/.toml/.yaml/metadata); review manually" });
            if (!result.Coverage.Passed)
            {
                result.Errors.Add("coverage scan found " + result.Coverage.StaleOldTokens + " stale old FormID token(s) in text files; output was not published");
                return result;
            }

            progress?.Report("Validating output");
            result.Validation = ConverterValidator.Validate(stagingPlugin, plan);
            if (!result.Validation.Passed)
            {
                result.Errors.Add("validation failed; output was not published");
                return result;
            }

            result.OutputFolder = outputFolder;
            result.OutputPluginPath = Path.Combine(outputFolder, plan.SourcePluginFileName);
            result.OutputPluginSha256 = HashFile(stagingPlugin);

            progress?.Report("Writing manifest and report");
            var manifestPath = Path.Combine(staging, "conversion-manifest.json");
            var reportPath = Path.Combine(staging, "CONVERSION-REPORT.md");
            ConversionReportWriter.WriteManifest(plan, result, stagingPlugin, manifestPath);
            ConversionReportWriter.WriteReport(plan, result, reportPath);

            if (Directory.Exists(outputFolder))
            {
                if (!options.Overwrite)
                {
                    result.Errors.Add("output folder already exists (use --overwrite): " + outputFolder);
                    return result;
                }
                Directory.Delete(outputFolder, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputFolder) ?? ".");
            Directory.Move(staging, outputFolder);
            staging = null;

            result.Success = true;
            result.ManifestPath = Path.Combine(outputFolder, "conversion-manifest.json");
            result.ReportPath = Path.Combine(outputFolder, "CONVERSION-REPORT.md");
            progress?.Report("Done: " + outputFolder);
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Errors.Add("conversion cancelled");
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
                try { Directory.Delete(staging, recursive: true); } catch { }
            }
        }
    }

    private static void CopyLooseFiles(string sourceFolder, string staging, string pluginFileName, string sourcePluginPath, List<WarningItem> warnings)
    {
        var pluginRelative = Path.GetRelativePath(sourceFolder, sourcePluginPath);
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, file);
            if (string.Equals(relative, pluginRelative, StringComparison.OrdinalIgnoreCase)) continue;
            var destination = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            var ext = Path.GetExtension(file);
            if (ext is ".esp" or ".esm" or ".esl" && !string.Equals(Path.GetFileName(file), pluginFileName, StringComparison.OrdinalIgnoreCase))
                warnings.Add(new WarningItem { Code = "EXTRA_PLUGIN_COPIED", Severity = Severity.Warning, Message = "additional plugin copied unchanged: " + relative });
        }
    }

    private static void ApplyAssetRenames(string staging, List<AssetRename> renames, List<WarningItem> warnings)
    {
        foreach (var rename in renames)
        {
            var oldPath = Path.Combine(staging, rename.OldRelativePath);
            var newPath = Path.Combine(staging, rename.NewRelativePath);
            if (!File.Exists(oldPath))
            {
                warnings.Add(new WarningItem { Code = "ASSET_RENAME_NOT_FOUND", Severity = Severity.Warning, Message = "asset to rename was not found (may be inside a BSA): " + rename.OldRelativePath });
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            if (File.Exists(newPath))
            {
                warnings.Add(new WarningItem { Code = "ASSET_RENAME_COLLISION", Severity = Severity.Warning, Message = "rename target already exists, leaving original: " + rename.NewRelativePath });
                continue;
            }
            File.Move(oldPath, newPath);
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

using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace SkyrimModSplitter.Core.Scanning;

public sealed class PluginScanner
{
    public PluginScanResult Scan(string pluginPath, RawPluginInfo raw, string relativePath)
    {
        var result = new PluginScanResult
        {
            FileName = Path.GetFileName(pluginPath),
            RelativePath = relativePath,
            FullPath = Path.GetFullPath(pluginPath),
            SizeBytes = raw.SizeBytes,
            Sha256 = raw.Sha256,
            IsEsl = raw.IsEsl,
            IsEsm = raw.IsEsm,
            IsLocalized = raw.IsLocalized,
            HeaderVersion = raw.HeaderVersion,
            HedrVersion = raw.HedrVersion,
            NextFormId = raw.NextFormId,
            Masters = raw.Masters.ToList(),
            RawRecordCount = raw.TotalRecords,
            RawStatedRecordCount = raw.NumRecordsStated,
            RawCountsMatchHeader = raw.CountsMatchHeader,
            RecordCountsBySignature = raw.RecordCounts,
        };
        foreach (var e in raw.Errors) result.Warnings.Add(new WarningItem { Code = "RAW_SCAN_ERROR", Severity = Severity.Error, Message = e });

        try
        {
            var mod = SkyrimMod.CreateFromBinary(pluginPath, SkyrimRelease.SkyrimSE);
            result.ParsedByMutagen = true;
            result.MutagenRecordCount = 0;
            var masterKeys = new HashSet<ModKey>(mod.ModHeader.MasterReferences.Select(m => m.Master));
            var seenKeys = new HashSet<FormKey>();
            var own = 0;
            var overrides = 0;
            var below = 0;
            var duplicates = 0;
            var minOwn = uint.MaxValue;
            uint maxOwn = 0;
            var links = new LinkStatistics();
            var vmadRows = new List<VmadScriptInfo>();

            foreach (var rec in mod.EnumerateMajorRecords())
            {
                result.MutagenRecordCount++;
                var fk = rec.FormKey;
                if (fk.ModKey.Equals(mod.ModKey))
                {
                    own++;
                    if (fk.ID < 0x800) below++;
                    if (fk.ID < minOwn) minOwn = fk.ID;
                    if (fk.ID > maxOwn) maxOwn = fk.ID;
                    if (!seenKeys.Add(fk)) duplicates++;
                }
                else
                {
                    overrides++;
                }

                if (rec is IFormLinkContainerGetter linkContainer)
                {
                    foreach (var link in linkContainer.EnumerateFormLinks())
                    {
                        links.Total++;
                        var target = link.FormKeyNullable;
                        if (target is null || target.Value.ModKey.Equals(ModKey.Null) || target.Value.ID == 0)
                        {
                            links.Unresolved++;
                            continue;
                        }
                        var key = target.Value;
                        if (key.ModKey.Equals(mod.ModKey)) links.Self++;
                        else if (masterKeys.Contains(key.ModKey)) links.Master++;
                        else links.Other++;
                    }
                }

                if (rec is IHaveVirtualMachineAdapterGetter vmadRec && vmadRec.VirtualMachineAdapter is { } vmad)
                {
                    foreach (var script in vmad.Scripts)
                    {
                        vmadRows.Add(new VmadScriptInfo
                        {
                            RecordFormKey = fk.ToString(),
                            RecordType = rec.GetType().Name,
                            ScriptName = script.Name ?? "",
                            PropertyCount = script.Properties?.Count ?? 0,
                        });
                    }
                }
            }

            result.OwnRecordCount = own;
            result.OverrideRecordCount = overrides;
            result.OwnIdsBelow0x800 = below;
            result.MinOwnFormId = minOwn == uint.MaxValue ? 0 : minOwn;
            result.MaxOwnFormId = maxOwn;
            result.DuplicateOwnFormIds = duplicates;
            result.Links = links;
            result.VmadScripts = vmadRows;
            result.EslSafe = own <= 2048 && below == 0 && (own == 0 || (minOwn >= 0x800 && maxOwn <= 0xFFF));
            result.RenumberRequired = (below > 0 || (own > 0 && (minOwn < 0x800 || maxOwn > 0xFFF))) && own <= 2048;
            result.SplitRequired = own > 2048;
            result.EslHeadroom = 2048 - own;

            if (raw.TotalRecords != result.MutagenRecordCount)
            {
                result.Warnings.Add(new WarningItem
                {
                    Code = "RECORD_COUNT_MISMATCH",
                    Severity = Severity.Error,
                    Message = "raw count " + raw.TotalRecords + " != Mutagen count " + result.MutagenRecordCount + "; record coverage is not proven",
                });
            }
            if (below > 0)
                result.Warnings.Add(new WarningItem { Code = "OWN_IDS_BELOW_0X800", Severity = Severity.Warning, Message = below + " own record IDs are below 0x800 (renumber required before ESL flag)" });
            if (own > 0 && maxOwn > 0xFFF)
                result.Warnings.Add(new WarningItem { Code = "OWN_IDS_ABOVE_0XFFF", Severity = Severity.Warning, Message = "own record IDs reach 0x" + maxOwn.ToString("X") + " (renumber required before ESL flag)" });
            if (own > 2048)
                result.Warnings.Add(new WarningItem { Code = "SPLIT_REQUIRED", Severity = Severity.Warning, Message = own + " own records > 2048 new-record ESL budget; split is required" });
            if (raw.IsLocalized)
                result.Warnings.Add(new WarningItem { Code = "LOCALIZED_PLUGIN", Severity = Severity.Error, Message = "localized plugin (STRINGS/DLSTRINGS/ILSTRINGS) is refused in v1" });
            if (duplicates > 0)
                result.Warnings.Add(new WarningItem { Code = "DUPLICATE_FORM_IDS", Severity = Severity.Error, Message = duplicates + " duplicate own FormIDs" });
        }
        catch (Exception ex)
        {
            result.ParseError = ex.Message;
            result.Warnings.Add(new WarningItem { Code = "PLUGIN_PARSE_FAILED", Severity = Severity.Error, Message = "Mutagen could not parse plugin: " + ex.Message });
        }

        return result;
    }
}

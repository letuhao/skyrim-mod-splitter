using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Conversion;

public sealed class ConversionPlanner
{
    public ConversionPlan Plan(ModScanResult scan, ConversionOptions options)
    {
        var target = scan.Plugins.FirstOrDefault(p => p.IsTarget)
            ?? throw new InvalidOperationException("scan has no target plugin");
        var plan = new ConversionPlan
        {
            SourceFolder = scan.ModFolder,
            SourcePluginFileName = target.FileName,
            SourcePluginPath = target.FullPath,
            SourcePluginSha256 = target.Sha256,
            SourceFolderHash = scan.ModFolderHash,
            OutputFolder = options.OutputFolder,
            OwnRecordCount = target.OwnRecordCount,
            OverrideRecordCount = target.OverrideRecordCount,
            RawRecordCount = target.RawRecordCount,
            SourceStatedRecordCount = target.RawStatedRecordCount,
            SourceMasters = target.Masters.ToList(),
            Warnings = target.Warnings.Concat(scan.Warnings.Where(w => w.Code is "ARCHIVE_COVERAGE" or "MASTERS_UNCHECKED")).ToList(),
        };
        plan.IsSplitRequired = target.SplitRequired;

        if (!target.ParsedByMutagen)
        {
            return Block(plan, "plugin could not be parsed: " + (target.ParseError ?? "unknown error"));
        }
        if (target.IsLocalized)
        {
            return Block(plan, "localized plugin (STRINGS/DLSTRINGS/ILSTRINGS) is refused in v1");
        }
        if (target.MissingMasters.Count > 0)
        {
            return Block(plan, "missing master files: " + string.Join(", ", target.MissingMasters));
        }
        if (target.DuplicateOwnFormIds > 0)
        {
            return Block(plan, "plugin contains duplicate own FormIDs");
        }
        if (target.IsEsm)
        {
            return Block(plan, "ESM light-master conversion is not enabled until the master-flag rule is verified");
        }
        if (target.SplitRequired)
        {
            plan.Verdict = ConversionVerdict.SplitRequired;
            plan.VerdictReason = target.OwnRecordCount + " new records > " + plan.EslBudget + " ESL budget; split planning is a later slice";
            plan.IsBlocked = true;
            plan.Warnings.Add(new WarningItem { Code = "SPLIT_REQUIRED", Severity = Severity.Warning, Message = plan.VerdictReason });
            return plan;
        }
        if (target.IsEsl && !target.EslSafe)
        {
            return Block(plan, "plugin is already ESL-flagged but its own IDs are not in the light-plugin range; refusing to touch it");
        }
        if (target.IsEsl)
        {
            plan.Verdict = ConversionVerdict.AlreadyLight;
            plan.VerdictReason = "plugin is already ESL-flagged and ESL-safe; nothing to renumber";
            plan.NeedsEslFlag = false;
            plan.NeedsRenumber = false;
            return plan;
        }
        if (target.EslSafe)
        {
            plan.Verdict = ConversionVerdict.EslReady;
            plan.VerdictReason = "all own IDs are already in 0x800-0xFFF; only the ESL flag and output rebuild are needed";
            plan.NeedsEslFlag = true;
            plan.NeedsRenumber = false;
            return plan;
        }

        plan.Verdict = ConversionVerdict.EslRenumber;
        plan.VerdictReason = "own record IDs are outside the ESL range; renumber " + target.OwnRecordCount + " records to 0x800+ and set the ESL flag";
        plan.NeedsEslFlag = true;
        plan.NeedsRenumber = true;

        // Build the deterministic mapping from the actual plugin records.
        var mod = SkyrimMod.CreateFromBinary(target.FullPath, SkyrimRelease.SkyrimSE);
        var ownRecords = new List<IMajorRecordGetter>();
        foreach (var rec in mod.EnumerateMajorRecords())
            if (rec.FormKey.ModKey.Equals(mod.ModKey)) ownRecords.Add(rec);
        ownRecords.Sort((a, b) =>
        {
            var c = a.FormKey.ID.CompareTo(b.FormKey.ID);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.GetType().Name, b.GetType().Name);
            return c != 0 ? c : string.CompareOrdinal(a.EditorID ?? "", b.EditorID ?? "");
        });
        if (ownRecords.Count != target.OwnRecordCount)
        {
            plan.Warnings.Add(new WarningItem
            {
                Code = "OWN_COUNT_MISMATCH",
                Severity = Severity.Error,
                Message = "scan own count " + target.OwnRecordCount + " != loaded own count " + ownRecords.Count,
            });
        }
        uint next = plan.FirstNewObjectId;
        foreach (var rec in ownRecords)
        {
            var newId = next++;
            plan.RecordMappings.Add(new RecordIdMapping
            {
                RecordType = rec.GetType().Name,
                EditorId = rec.EditorID,
                OldObjectId = rec.FormKey.ID,
                NewObjectId = newId,
                OldFormKey = rec.FormKey.ToString(),
                NewFormKey = new FormKey(mod.ModKey, newId).ToString(),
            });
        }
        plan.AssetRenames = FaceGenRewriter.BuildRenames(scan, plan);
        return plan;
    }

    private static ConversionPlan Block(ConversionPlan plan, string reason)
    {
        plan.Verdict = ConversionVerdict.Blocked;
        plan.VerdictReason = reason;
        plan.IsBlocked = true;
        plan.Warnings.Add(new WarningItem { Code = "CONVERSION_BLOCKED", Severity = Severity.Error, Message = reason });
        return plan;
    }
}

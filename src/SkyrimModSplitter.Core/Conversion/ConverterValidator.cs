using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Conversion;

public static class ConverterValidator
{
    private const int MaxFailureSamples = 20;

    public static ValidationSummary Validate(string pluginPath, ConversionPlan plan)
    {
        var summary = new ValidationSummary();
        try
        {
            var raw = RawPluginScanner.Scan(pluginPath);
            summary.Checks.Add("raw plugin parse: records=" + raw.TotalRecords + ", esl=" + raw.IsEsl + ", esm=" + raw.IsEsm);
            if (!raw.IsEsl) summary.Failures.Add("TES4 ESL flag is not set in the written plugin");
            if (raw.TotalRecords != plan.RawRecordCount) summary.Failures.Add("record count changed: expected " + plan.RawRecordCount + ", got " + raw.TotalRecords);

            if (!raw.Masters.SequenceEqual(plan.SourceMasters, StringComparer.OrdinalIgnoreCase))
            {
                summary.Failures.Add("master list changed: expected [" + string.Join(", ", plan.SourceMasters) + "], got [" + string.Join(", ", raw.Masters) + "]");
            }
            summary.Checks.Add("masters preserved: " + raw.Masters.Count + " entries");
            if (plan.SourceStatedRecordCount != 0 && raw.NumRecordsStated != plan.SourceStatedRecordCount)
                summary.Failures.Add("HEDR stated record count changed: expected " + plan.SourceStatedRecordCount + ", got " + raw.NumRecordsStated);

            var mod = SkyrimMod.CreateFromBinary(pluginPath, SkyrimRelease.SkyrimSE);
            var allRecords = mod.EnumerateMajorRecords().ToList();
            var outKey = mod.ModKey;
            summary.Checks.Add("Mutagen parse: records=" + allRecords.Count + ", flags=" + mod.ModHeader.Flags + ", nextFormId=0x" + mod.ModHeader.Stats.NextFormID.ToString("X"));
            if (!mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Small)) summary.Failures.Add("Mutagen does not see the Small (ESL) header flag");

            var own = allRecords.Where(r => r.FormKey.ModKey.Equals(outKey)).ToList();
            summary.Checks.Add("own records=" + own.Count + ", overrides=" + (allRecords.Count - own.Count));
            if (own.Count != plan.OwnRecordCount) summary.Failures.Add("own record count: expected " + plan.OwnRecordCount + ", got " + own.Count);
            if (allRecords.Count - own.Count != plan.OverrideRecordCount) summary.Failures.Add("override record count: expected " + plan.OverrideRecordCount + ", got " + (allRecords.Count - own.Count));

            var ownIds = own.Select(r => r.FormKey.ID).ToList();
            if (ownIds.Count != ownIds.Distinct().Count()) summary.Failures.Add("duplicate own FormIDs exist in the output");
            var outOfRange = ownIds.Where(id => id < 0x800 || id > 0xFFF).Take(MaxFailureSamples).ToList();
            if (outOfRange.Count > 0) summary.Failures.Add("own FormIDs outside 0x800-0xFFF: " + string.Join(", ", outOfRange.Select(id => "0x" + id.ToString("X"))));
            var ownSet = ownIds.ToHashSet();
            if (ownIds.Count > plan.EslBudget) summary.Failures.Add("own records " + ownIds.Count + " exceed ESL budget " + plan.EslBudget);
            var maxId = ownIds.Count == 0 ? 0u : ownIds.Max();
            if (mod.ModHeader.Stats.NextFormID <= maxId && ownIds.Count > 0) summary.Failures.Add("NextFormID 0x" + mod.ModHeader.Stats.NextFormID.ToString("X") + " <= max own ID 0x" + maxId.ToString("X"));
            if (mod.ModHeader.Stats.NextFormID > 0x1000) summary.Failures.Add("ESL NextFormID 0x" + mod.ModHeader.Stats.NextFormID.ToString("X") + " exceeds the 12-bit light-plugin space (0x1000)");

            if (plan.NeedsRenumber)
            {
                var missing = plan.RecordMappings.Where(m => !ownSet.Contains(m.NewObjectId)).Take(MaxFailureSamples).ToList();
                if (missing.Count > 0) summary.Failures.Add("planned new FormIDs missing from output: " + string.Join(", ", missing.Select(m => "0x" + m.NewObjectId.ToString("X"))));
                summary.Checks.Add("mapping coverage: " + plan.RecordMappings.Count + " planned new IDs");
            }

            var masterNames = new HashSet<string>(raw.Masters, StringComparer.OrdinalIgnoreCase);
            var dangling = 0;
            var unknownMasterRefs = 0;
            foreach (var rec in allRecords)
            {
                if (rec is not IFormLinkContainerGetter linkContainer) continue;
                foreach (var link in linkContainer.EnumerateFormLinks())
                {
                    var target = link.FormKeyNullable;
                    if (target is null)
                    {
                        dangling++;
                        if (summary.Failures.Count < MaxFailureSamples)
                            summary.Failures.Add("null link from " + rec.FormKey + " (" + rec.GetType().Name + ")");
                        continue;
                    }
                    var key = target.Value;
                    if (key.ModKey.Equals(outKey))
                    {
                        if (!ownSet.Contains(key.ID))
                        {
                            dangling++;
                            if (summary.Failures.Count < MaxFailureSamples)
                                summary.Failures.Add("self-link points to missing own FormID 0x" + key.ID.ToString("X") + " from " + rec.FormKey + " (" + rec.GetType().Name + ")");
                        }
                    }
                    else if (!key.ModKey.Equals(ModKey.Null) && !masterNames.Contains(key.ModKey.FileName))
                    {
                        unknownMasterRefs++;
                        if (summary.Failures.Count < MaxFailureSamples)
                            summary.Failures.Add("link to non-master plugin " + key.ModKey.FileName + " from " + rec.FormKey + " (" + rec.GetType().Name + ")");
                    }
                }
            }
            summary.Checks.Add("self-link integrity: dangling=" + dangling + ", unknown-master-refs=" + unknownMasterRefs);

            if (plan.NeedsRenumber && plan.RecordMappings.Count > 0)
                ValidateSourceCorrespondence(plan, own, outKey, summary);
        }
        catch (Exception ex)
        {
            summary.Failures.Add("validation threw: " + ex.Message);
        }
        summary.Passed = summary.Failures.Count == 0;
        return summary;
    }

    private static void ValidateSourceCorrespondence(ConversionPlan plan, IReadOnlyList<IMajorRecord> outputOwn, ModKey outKey, ValidationSummary summary)
    {
        if (!File.Exists(plan.SourcePluginPath))
        {
            summary.Failures.Add("source correspondence: source plugin no longer exists at " + plan.SourcePluginPath);
            return;
        }
        var map = plan.RecordMappings.ToDictionary(m => m.OldObjectId, m => m.NewObjectId);
        var sourceMod = SkyrimMod.CreateFromBinary(plan.SourcePluginPath, SkyrimRelease.SkyrimSE);
        var sourceOwn = sourceMod.EnumerateMajorRecords()
            .Where(r => r.FormKey.ModKey.Equals(sourceMod.ModKey))
            .ToDictionary(r => r.FormKey.ID);
        var outputOwnById = outputOwn.ToDictionary(r => r.FormKey.ID);
        var compared = 0;
        var failures = 0;
        foreach (var mapping in plan.RecordMappings)
        {
            if (!sourceOwn.TryGetValue(mapping.OldObjectId, out var sourceRecord))
            {
                summary.Failures.Add("source correspondence: source record 0x" + mapping.OldObjectId.ToString("X") + " not found");
                continue;
            }
            if (!outputOwnById.TryGetValue(mapping.NewObjectId, out var outputRecord))
            {
                summary.Failures.Add("source correspondence: output record 0x" + mapping.NewObjectId.ToString("X") + " not found");
                continue;
            }
            if (sourceRecord.GetType().Name != outputRecord.GetType().Name)
            {
                summary.Failures.Add("source correspondence: type changed for 0x" + mapping.OldObjectId.ToString("X") + ": " + sourceRecord.GetType().Name + " -> " + outputRecord.GetType().Name);
                continue;
            }
            if (!string.Equals(sourceRecord.EditorID ?? "", outputRecord.EditorID ?? "", StringComparison.Ordinal))
                summary.Failures.Add("source correspondence: EditorID changed for 0x" + mapping.OldObjectId.ToString("X") + ": '" + (sourceRecord.EditorID ?? "") + "' -> '" + (outputRecord.EditorID ?? "") + "'");
            if (sourceRecord is IFormLinkContainerGetter sourceLinks && outputRecord is IFormLinkContainerGetter outputLinks)
            {
                var expected = LinkMultiplicity(sourceLinks, sourceMod.ModKey, map, outKey);
                var actual = LinkMultiplicity(outputLinks, outKey, null, outKey);
                if (!MultiplicityEquals(expected, actual))
                {
                    failures++;
                    if (summary.Failures.Count < MaxFailureSamples)
                        summary.Failures.Add("source correspondence: link set mismatch for record 0x" + mapping.OldObjectId.ToString("X") + " -> 0x" + mapping.NewObjectId.ToString("X"));
                }
            }
            compared++;
        }
        summary.Checks.Add("source correspondence: compared=" + compared + ", link-set mismatches=" + failures);
    }

    private static Dictionary<string, int> LinkMultiplicity(IFormLinkContainerGetter container, ModKey selfKey, IReadOnlyDictionary<uint, uint>? remap, ModKey targetKey)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var link in container.EnumerateFormLinks())
        {
            var target = link.FormKeyNullable;
            string key;
            if (target is null) key = "<null>";
            else if (remap != null && target.Value.ModKey.Equals(selfKey) && remap.TryGetValue(target.Value.ID, out var newId))
                key = targetKey.FileName + "|0x" + newId.ToString("X");
            else key = target.Value.ModKey.FileName + "|0x" + target.Value.ID.ToString("X");
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        return counts;
    }

    private static bool MultiplicityEquals(Dictionary<string, int> expected, Dictionary<string, int> actual)
    {
        if (expected.Count != actual.Count) return false;
        foreach (var kv in expected)
            if (!actual.TryGetValue(kv.Key, out var count) || count != kv.Value) return false;
        return true;
    }
}

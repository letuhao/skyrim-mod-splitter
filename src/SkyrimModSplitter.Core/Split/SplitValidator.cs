using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace SkyrimModSplitter.Core.Split;

public static class SplitValidator
{
    public static SplitValidation Validate(SplitPlan plan, string originalPath, string masterPath, ModKey originalKey, ModKey masterKey)
    {
        var validation = new SplitValidation();
        try
        {
            var originalRaw = Scanning.RawPluginScanner.Scan(originalPath);
            var masterRaw = Scanning.RawPluginScanner.Scan(masterPath);
            validation.Checks.Add("raw parse: original=" + originalRaw.TotalRecords + ", master=" + masterRaw.TotalRecords);
            if (!originalRaw.IsEsl) validation.Failures.Add("original part is not ESL-flagged");
            if (!masterRaw.IsEsl) validation.Failures.Add("master part is not ESL-flagged");
            if (!masterRaw.IsEsm) validation.Failures.Add("master part is not ESM-flagged (required as a master)");

            var expectedOriginal = plan.Parts[0].OwnRecordCount;
            var expectedMaster = plan.Parts[1].OwnRecordCount;
            var masterMod = SkyrimMod.CreateFromBinary(masterPath, SkyrimRelease.SkyrimSE);
            var originalMod = SkyrimMod.CreateFromBinary(originalPath, SkyrimRelease.SkyrimSE);
            var masterOwn = masterMod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey.Equals(masterKey)).ToList();
            var originalOwn = originalMod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey.Equals(originalKey)).ToList();
            validation.Checks.Add("own counts: master=" + masterOwn.Count + " (expected " + expectedMaster + "), original=" + originalOwn.Count + " (expected " + expectedOriginal + ")");
            if (masterOwn.Count != expectedMaster) validation.Failures.Add("master own count mismatch");
            if (originalOwn.Count != expectedOriginal) validation.Failures.Add("original own count mismatch");

            var masterIds = masterOwn.Select(r => r.FormKey.ID).ToHashSet();
            var originalIds = originalOwn.Select(r => r.FormKey.ID).ToHashSet();
            CheckIds(masterIds, "master", validation);
            CheckIds(originalIds, "original", validation);

            if (!masterRaw.Masters.SequenceEqual(plan.Parts[1].Masters, StringComparer.OrdinalIgnoreCase))
                validation.Failures.Add("master part master list mismatch: [" + string.Join(", ", masterRaw.Masters) + "]");
            if (!originalRaw.Masters.SequenceEqual(plan.Parts[0].Masters, StringComparer.OrdinalIgnoreCase))
                validation.Failures.Add("original part master list mismatch: [" + string.Join(", ", originalRaw.Masters) + "]");

            var originalMasters = new HashSet<string>(originalRaw.Masters, StringComparer.OrdinalIgnoreCase);
            var masterMasters = new HashSet<string>(masterRaw.Masters, StringComparer.OrdinalIgnoreCase);
            var originalSelf = 0;
            foreach (var record in originalOwn)
            {
                if (record is not IFormLinkContainerGetter links) continue;
                foreach (var link in links.EnumerateFormLinks())
                {
                    var target = link.FormKeyNullable;
                    if (target is null) continue;
                    var key = target.Value;
                    if (key.ModKey.Equals(ModKey.Null) || key.ID == 0) continue;
                    if (key.ModKey.Equals(originalKey))
                    {
                        originalSelf++;
                        if (!originalIds.Contains(key.ID)) validation.Failures.Add("original self-link missing: 0x" + key.ID.ToString("X"));
                    }
                    else if (key.ModKey.Equals(masterKey))
                    {
                        if (!masterIds.Contains(key.ID)) validation.Failures.Add("original link to missing master record: 0x" + key.ID.ToString("X"));
                    }
                    else if (!originalMasters.Contains(key.ModKey.FileName))
                    {
                        validation.Failures.Add("original link to non-master plugin " + key.ModKey.FileName);
                    }
                }
            }
            var masterLinks = 0;
            foreach (var record in masterOwn)
            {
                if (record is not IFormLinkContainerGetter links) continue;
                foreach (var link in links.EnumerateFormLinks())
                {
                    var target = link.FormKeyNullable;
                    if (target is null) continue;
                    var key = target.Value;
                    if (key.ModKey.Equals(ModKey.Null) || key.ID == 0) continue;
                    if (key.ModKey.Equals(masterKey))
                    {
                        masterLinks++;
                        if (!masterIds.Contains(key.ID)) validation.Failures.Add("master self-link missing: 0x" + key.ID.ToString("X"));
                    }
                    else if (key.ModKey.Equals(originalKey))
                    {
                        validation.Failures.Add("master part references the dependent original part (cycle): record " + record.FormKey);
                    }
                    else if (!masterMasters.Contains(key.ModKey.FileName))
                    {
                        validation.Failures.Add("master link to non-master plugin " + key.ModKey.FileName);
                    }
                }
            }
            validation.Checks.Add("original self-links=" + originalSelf + ", master self-links=" + masterLinks);
            validation.Checks.Add("load order: " + string.Join(" -> ", plan.LoadOrder));
            if (plan.LoadOrder.Count != 2 || plan.LoadOrder[0] != plan.Parts[1].FileName)
                validation.Failures.Add("load order does not place the master part first");
        }
        catch (Exception ex)
        {
            validation.Failures.Add("split validation threw: " + ex.Message);
        }
        validation.Passed = validation.Failures.Count == 0;
        return validation;
    }

    private static void CheckIds(HashSet<uint> ids, string label, SplitValidation validation)
    {
        if (ids.Count != ids.Distinct().Count()) validation.Failures.Add(label + " has duplicate own IDs");
        var outOfRange = ids.Where(id => id < 0x800 || id > 0xFFF).Take(10).ToList();
        if (outOfRange.Count > 0) validation.Failures.Add(label + " IDs outside 0x800-0xFFF: " + string.Join(",", outOfRange.Select(id => "0x" + id.ToString("X"))));
        if (ids.Count > 2048) validation.Failures.Add(label + " exceeds ESL budget: " + ids.Count);
    }
}

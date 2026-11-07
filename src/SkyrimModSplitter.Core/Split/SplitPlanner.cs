using System.Collections;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Scanning;

namespace SkyrimModSplitter.Core.Split;

/// <summary>
/// Plans an ESL-capable two-part split of one plugin.
///
/// Partition model: the new "Part 2" plugin is a dependency-closed master that
/// loads first; the original filename is kept by the dependent part, which may
/// reference Part 2 but never the reverse. Cell units (CellBlocks are not flat
/// groups) are forced into the original part; Part 2 only contains flat records
/// whose dependencies are also in Part 2, so it can be built as a new plugin.
/// </summary>
public sealed class SplitPlanner
{
    private sealed class Unit
    {
        public List<IMajorRecordGetter> Records { get; } = new();
        public bool IsCell { get; set; }
        public int Weight => Records.Count;
    }

    public SplitPlan Plan(SplitOptions options)
    {
        var plan = new SplitPlan
        {
            SourceFolder = Path.GetFullPath(options.ModFolder),
            OutputFolder = options.OutputFolder,
            EslBudget = options.EslBudget,
        };
        var pluginPath = ResolvePlugin(plan.SourceFolder, options.Plugin);
        if (pluginPath == null)
        {
            plan.BlockedReason = "no plugin found";
            return plan;
        }
        plan.SourcePluginPath = Path.GetFullPath(pluginPath);
        plan.SourcePluginFileName = Path.GetFileName(pluginPath);
        var raw = RawPluginScanner.Scan(pluginPath);
        plan.SourceSha256 = raw.Sha256;
        plan.SourceMasters = raw.Masters.ToList();
        if (raw.IsEsm) { plan.BlockedReason = "ESM splitting is not enabled; split only classic ESP overhauls"; return plan; }
        if (raw.IsLocalized) { plan.BlockedReason = "localized plugins are refused in v1"; return plan; }
        var mod = SkyrimMod.CreateFromBinary(pluginPath, SkyrimRelease.SkyrimSE);
        var ownRecords = mod.EnumerateMajorRecords()
            .Where(r => r.FormKey.ModKey.Equals(mod.ModKey))
            .ToList();
        plan.TotalOwnRecords = ownRecords.Count;
        if (ownRecords.Count <= options.EslBudget)
        {
            plan.BlockedReason = "plugin already fits one ESL; use the normal conversion path";
            return plan;
        }

        var units = new List<Unit>();
        var unitByFormKey = new Dictionary<FormKey, int>();

        void AddUnitRecord(int unitIndex, IMajorRecordGetter record)
        {
            if (unitByFormKey.ContainsKey(record.FormKey)) return;
            unitByFormKey[record.FormKey] = unitIndex;
            units[unitIndex].Records.Add(record);
        }

        int CreateUnit(IMajorRecordGetter record, bool isCell)
        {
            var index = units.Count;
            units.Add(new Unit { IsCell = isCell });
            units[index].Records.Add(record);
            unitByFormKey[record.FormKey] = index;
            return index;
        }

        foreach (var prop in typeof(ISkyrimMod).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.PropertyType.IsGenericType) continue;
            if (!typeof(IMajorRecordGetter).IsAssignableFrom(prop.PropertyType.GetGenericArguments()[0])) continue;
            if (prop.GetValue(mod) is not IEnumerable enumerable) continue;
            foreach (var item in enumerable)
            {
                var record = Extract(item);
                if (record == null || !record.FormKey.ModKey.Equals(mod.ModKey)) continue;
                if (!unitByFormKey.ContainsKey(record.FormKey)) CreateUnit(record, isCell: false);
            }
        }

        foreach (var cellGroupItem in mod.Cells)
            TraverseCellBlocks(cellGroupItem);

        void TraverseCellBlocks(object? node)
        {
            if (node == null) return;
            if (node is ICellGetter cell) { EnsureCellUnit(cell); return; }
            if (node is IEnumerable && node is not string)
            {
                foreach (var item in (IEnumerable)node)
                {
                    if (item == null) continue;
                    if (item is ICellGetter c) EnsureCellUnit(c);
                    else TraverseCellBlocks(item);
                }
                return;
            }
            foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                object? value;
                try { value = prop.GetValue(node); } catch { continue; }
                if (value == null || value is string || value is byte[]) continue;
                if (value is ICellGetter directCell) { EnsureCellUnit(directCell); continue; }
                if (value is IEnumerable en)
                {
                    foreach (var item in en)
                    {
                        if (item == null) continue;
                        if (item is ICellGetter c) { EnsureCellUnit(c); continue; }
                        var asRecord = Extract(item);
                        if (asRecord is ICellGetter cell2) EnsureCellUnit(cell2);
                        else TraverseCellBlocks(item);
                    }
                }
            }
        }

        void EnsureCellUnit(ICellGetter cell)
        {
            int index;
            if (unitByFormKey.TryGetValue(cell.FormKey, out var existing))
            {
                index = existing;
                units[index].IsCell = true;
            }
            else if (cell.FormKey.ModKey.Equals(mod.ModKey))
            {
                index = CreateUnit(cell, isCell: true);
            }
            else
            {
                // Master-cell override: keep the override out of units (it stays an
                // override in the original part) but map its own placed children into
                // a forced cell unit so they stay with the cells.
                index = units.Count;
                units.Add(new Unit { IsCell = true });
            }
            MapNested(cell, index);
        }

        void MapNested(object? obj, int unitIndex)
        {
            if (obj == null) return;
            if (obj is IMajorRecordGetter record && record.FormKey.ID != 0
                && record.FormKey.ModKey.Equals(mod.ModKey) && !unitByFormKey.ContainsKey(record.FormKey))
            {
                AddUnitRecord(unitIndex, record);
            }
            foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                object? value;
                try { value = prop.GetValue(obj); } catch { continue; }
                if (value == null || value is string || value is byte[]) continue;
                if (value is IMajorRecordGetter child) { MapNested(child, unitIndex); continue; }
                if (value is IEnumerable en)
                {
                    foreach (var item in en)
                    {
                        var nested = Extract(item);
                        if (nested != null) MapNested(nested, unitIndex);
                    }
                }
            }
        }

        foreach (var record in ownRecords)
            if (!unitByFormKey.ContainsKey(record.FormKey))
                CreateUnit(record, isCell: false);

        var ownKeys = ownRecords.Select(r => r.FormKey).ToHashSet();
        if (ownKeys.Count != unitByFormKey.Count || ownKeys.Any(k => !unitByFormKey.ContainsKey(k)))
        {
            plan.BlockedReason = "unit construction mismatch: own=" + ownKeys.Count + " unitKeys=" + unitByFormKey.Count;
            return plan;
        }
        plan.Warnings.Add(new WarningItem
        {
            Code = "SPLIT_UNITS",
            Severity = Severity.Info,
            Message = "units=" + units.Count + " (cell units=" + units.Count(u => u.IsCell) + ", own records=" + ownRecords.Count + ")",
        });

        var recordByKey = ownRecords.ToDictionary(r => r.FormKey);
        var adj = new HashSet<int>[units.Count];
        for (var i = 0; i < units.Count; i++) adj[i] = new HashSet<int>();
        foreach (var record in ownRecords)
        {
            var from = unitByFormKey[record.FormKey];
            if (record is not IFormLinkContainerGetter links) continue;
            foreach (var link in links.EnumerateFormLinks())
            {
                var target = link.FormKeyNullable;
                if (target is null || target.Value.ModKey.Equals(ModKey.Null) || target.Value.ID == 0) continue;
                if (!target.Value.ModKey.Equals(mod.ModKey)) continue;
                if (!recordByKey.ContainsKey(target.Value)) continue;
                var to = unitByFormKey[target.Value];
                if (to != from) adj[from].Add(to);
            }
        }

        var indices = Enumerable.Repeat(-1, units.Count).ToArray();
        var low = new int[units.Count];
        var onStack = new bool[units.Count];
        var stack = new Stack<int>();
        var component = Enumerable.Repeat(-1, units.Count).ToArray();
        var components = new List<List<int>>();
        var nextIndex = 0;
        void Strong(int v)
        {
            indices[v] = low[v] = nextIndex++;
            stack.Push(v); onStack[v] = true;
            foreach (var w in adj[v])
            {
                if (indices[w] == -1) { Strong(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack[w]) low[v] = Math.Min(low[v], indices[w]);
            }
            if (low[v] == indices[v])
            {
                var members = new List<int>();
                int w;
                do { w = stack.Pop(); onStack[w] = false; component[w] = components.Count; members.Add(w); } while (w != v);
                components.Add(members);
            }
        }
        for (var i = 0; i < units.Count; i++) if (indices[i] == -1) Strong(i);

        var compWeight = components.Select(c => c.Sum(u => units[u].Weight)).ToArray();
        var compAdj = new HashSet<int>[components.Count];
        for (var i = 0; i < components.Count; i++) compAdj[i] = new HashSet<int>();
        for (var u = 0; u < units.Count; u++)
            foreach (var v in adj[u])
                if (component[u] != component[v]) compAdj[component[u]].Add(component[v]);

        var post = new List<int>();
        var visited = new bool[components.Count];
        void Visit(int c)
        {
            if (visited[c]) return;
            visited[c] = true;
            foreach (var t in compAdj[c].OrderBy(x => x)) Visit(t);
            post.Add(c);
        }
        foreach (var c in Enumerable.Range(0, components.Count).OrderBy(i => components[i].Min())) Visit(c);

        var cellComps = new HashSet<int>();
        for (var u = 0; u < units.Count; u++)
            if (units[u].IsCell) cellComps.Add(component[u]);

        // Part 2 (new master) = dependency-closed flat components, loaded first.
        var masterComps = new HashSet<int>();
        var masterWeight = 0;
        foreach (var c in post)
        {
            if (cellComps.Contains(c)) continue;
            if (masterWeight + compWeight[c] > options.EslBudget) continue;
            if (compAdj[c].Any(t => !masterComps.Contains(t))) continue;
            masterComps.Add(c);
            masterWeight += compWeight[c];
        }
        var originalWeight = plan.TotalOwnRecords - masterWeight;
        if (masterWeight <= 0 || originalWeight > options.EslBudget || masterWeight > options.EslBudget)
        {
            plan.BlockedReason = "cannot partition within ESL budget (master=" + masterWeight + ", original=" + originalWeight + ", cells=" + cellComps.Count + ")";
            return plan;
        }
        if (cellComps.Any(c => masterComps.Contains(c)))
        {
            plan.BlockedReason = "cell component leaked into the new master part";
            return plan;
        }

        var partOriginal = new SplitPartPlan
        {
            Index = 0,
            FileName = plan.SourcePluginFileName,
            SourceFileName = plan.SourcePluginFileName,
            KeepsOriginalName = true,
            IsMaster = false,
            OverrideRecordCount = raw.TotalRecords - plan.TotalOwnRecords,
            Masters = plan.SourceMasters.ToList(),
            OwnRecordCount = originalWeight,
        };
        var partMaster = new SplitPartPlan
        {
            Index = 1,
            FileName = Path.GetFileNameWithoutExtension(plan.SourcePluginFileName) + " - Part 2" + Path.GetExtension(plan.SourcePluginFileName),
            SourceFileName = plan.SourcePluginFileName,
            KeepsOriginalName = false,
            IsMaster = true,
            OverrideRecordCount = 0,
            Masters = plan.SourceMasters.ToList(),
            OwnRecordCount = masterWeight,
        };
        partOriginal.Masters.Add(partMaster.FileName);
        plan.Parts.Add(partOriginal);
        plan.Parts.Add(partMaster);
        plan.LoadOrder = new List<string> { partMaster.FileName, partOriginal.FileName };
        plan.Warnings.Add(new WarningItem
        {
            Code = "SPLIT_PARTITION",
            Severity = Severity.Info,
            Message = "new master part=" + masterWeight + " own (loaded first); original name part=" + originalWeight + " own (loads after master)",
        });

        var recordsByPart = new Dictionary<int, List<IMajorRecordGetter>>
        {
            [partOriginal.Index] = new List<IMajorRecordGetter>(),
            [partMaster.Index] = new List<IMajorRecordGetter>(),
        };
        foreach (var unitIndex in Enumerable.Range(0, units.Count))
        {
            var c = component[unitIndex];
            var part = masterComps.Contains(c) ? partMaster : partOriginal;
            recordsByPart[part.Index].AddRange(units[unitIndex].Records);
            foreach (var record in units[unitIndex].Records)
                part.UnitFormKeys.Add(record.FormKey.ToString());
        }
        foreach (var part in plan.Parts)
        {
            var records = recordsByPart[part.Index]
                .OrderBy(r => r.FormKey.ID)
                .ThenBy(r => r.GetType().Name, StringComparer.Ordinal)
                .ToList();
            if (records.Count > options.EslBudget)
            {
                plan.BlockedReason = "part " + part.FileName + " has " + records.Count + " own records > budget";
                return plan;
            }
            var next = 0x800u;
            foreach (var record in records)
            {
                plan.Assignments.Add(new SplitRecordAssignment
                {
                    OldObjectId = record.FormKey.ID,
                    OldFormKey = record.FormKey.ToString(),
                    RecordType = record.GetType().Name,
                    EditorId = record.EditorID,
                    PartIndex = part.Index,
                    NewObjectId = next++,
                });
            }
        }
        if (plan.Assignments.Count != plan.TotalOwnRecords)
        {
            plan.BlockedReason = "assignment count " + plan.Assignments.Count + " != own records " + plan.TotalOwnRecords;
            return plan;
        }
        plan.Feasible = true;
        return plan;
    }

    private static IMajorRecordGetter? Extract(object? item)
    {
        if (item is IMajorRecordGetter record) return record;
        return item?.GetType().GetProperty("Value")?.GetValue(item) as IMajorRecordGetter;
    }

    private static string? ResolvePlugin(string folder, string? requested)
    {
        if (!Directory.Exists(folder)) return null;
        var plugins = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".esp", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plugins.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = plugins.FirstOrDefault(p => string.Equals(Path.GetFileName(p), requested, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return plugins.OrderByDescending(p => new FileInfo(p).Length).First();
    }
}

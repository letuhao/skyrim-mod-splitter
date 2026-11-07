using System;
using System.IO;
using System.Linq;
using SkyrimModSplitter.Core.Split;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class SplitPlannerTests
{
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SkyrimModSplitter.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void CardWeapon_Plan_TwoEslPartsFeasible()
    {
        var root = FindRepoRoot();
        if (root == null) return;
        var corpus = Path.Combine(root, "test-corpus", "CardWeapon");
        if (!Directory.Exists(corpus)) return;
        var plan = new SplitPlanner().Plan(new SplitOptions
        {
            ModFolder = corpus,
            OutputFolder = Path.Combine(Path.GetTempPath(), "sms-tests", "split-plan-" + Guid.NewGuid().ToString("N")),
        });
        Assert.True(plan.Feasible, plan.BlockedReason + " | " + string.Join(" || ", plan.Warnings.Select(w => w.Message)));
        Assert.Equal(2, plan.Parts.Count);
        Assert.Equal(3367, plan.TotalOwnRecords);
        Assert.Equal(plan.TotalOwnRecords, plan.Parts[0].OwnRecordCount + plan.Parts[1].OwnRecordCount);
        Assert.True(plan.Parts[0].OwnRecordCount <= 2048);
        Assert.True(plan.Parts[1].OwnRecordCount <= 2048);
        Assert.Equal(plan.SourcePluginFileName, plan.Parts[0].FileName);
        Assert.Contains("Part 2", plan.Parts[1].FileName);
        Assert.False(plan.Parts[0].IsMaster);
        Assert.True(plan.Parts[1].IsMaster);
        Assert.Contains(plan.Parts[1].FileName, plan.Parts[0].Masters);
        Assert.Equal(2, plan.LoadOrder.Count);
        Assert.Equal(plan.Parts[1].FileName, plan.LoadOrder[0]);
        Assert.Equal(plan.Parts[0].FileName, plan.LoadOrder[1]);
        Assert.Equal(plan.TotalOwnRecords, plan.Assignments.Count);

        var placed = plan.Assignments.Where(a => a.RecordType.Contains("Placed", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(placed);
        Assert.All(placed, a => Assert.Equal(0, a.PartIndex));
        Assert.DoesNotContain(plan.Assignments.Where(a => a.PartIndex == 1), a => a.RecordType.Contains("Placed", StringComparison.OrdinalIgnoreCase));
        foreach (var part in plan.Parts)
        {
            var ids = plan.Assignments.Where(a => a.PartIndex == part.Index).Select(a => a.NewObjectId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.InRange(id, 0x800u, 0xFFFu));
            Assert.True(ids.Count > 0);
        }
    }
}

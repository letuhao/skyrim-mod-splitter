using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SkyrimModSplitter.Core.Scanning;
using SkyrimModSplitter.Core.Split;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class SplitExecutorTests
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

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    [Fact]
    public void CardWeapon_Split_Apply_ProducesValidDeterministicParts()
    {
        var root = FindRepoRoot();
        if (root == null) return;
        var corpus = Path.Combine(root, "test-corpus", "CardWeapon");
        if (!Directory.Exists(corpus)) return;
        var sourcePlugin = Path.Combine(corpus, "CardWeapon_SE.esp");
        var sourceSha = Sha256(sourcePlugin);
        var output1 = Path.Combine(Path.GetTempPath(), "sms-tests", "split-real-1-" + Guid.NewGuid().ToString("N"));
        var options = new SplitOptions { ModFolder = corpus, OutputFolder = output1 };
        var plan = new SplitPlanner().Plan(options);
        Assert.True(plan.Feasible, plan.BlockedReason);
        var result = new SplitExecutor().Execute(plan, options);
        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.True(result.Validation.Passed, string.Join(" | ", result.Validation.Failures));
        Assert.Equal(sourceSha, Sha256(sourcePlugin));

        var masterPath = Path.Combine(output1, plan.Parts[1].FileName);
        var originalPath = Path.Combine(output1, plan.Parts[0].FileName);
        var masterRaw = RawPluginScanner.Scan(masterPath);
        var originalRaw = RawPluginScanner.Scan(originalPath);
        Assert.True(masterRaw.IsEsl && masterRaw.IsEsm);
        Assert.True(originalRaw.IsEsl && !originalRaw.IsEsm);
        Assert.Equal(2048, masterRaw.TotalRecords);
        Assert.Equal(1319 + 6, originalRaw.TotalRecords);
        Assert.Equal("Skyrim.esm, Update.esm", string.Join(", ", masterRaw.Masters));
        Assert.Equal("Skyrim.esm, Update.esm, " + plan.Parts[1].FileName, string.Join(", ", originalRaw.Masters));

        var output2 = Path.Combine(Path.GetTempPath(), "sms-tests", "split-real-2-" + Guid.NewGuid().ToString("N"));
        var plan2 = new SplitPlanner().Plan(new SplitOptions { ModFolder = corpus, OutputFolder = output2 });
        var result2 = new SplitExecutor().Execute(plan2, new SplitOptions { ModFolder = corpus, OutputFolder = output2 });
        Assert.True(result2.Success);
        Assert.Equal(Sha256(masterPath), Sha256(Path.Combine(output2, plan2.Parts[1].FileName)));
        Assert.Equal(Sha256(originalPath), Sha256(Path.Combine(output2, plan2.Parts[0].FileName)));
    }
}

using System;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Scanning;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class SyntheticFixture
{
    public static string CreatePlugin(string modKeyName = "Synthetic.esp", int weaponCount = 3)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sms-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mod = new SkyrimMod(ModKey.FromNameAndExtension(modKeyName), SkyrimRelease.SkyrimSE);
        for (var i = 0; i < weaponCount; i++)
        {
            var w = mod.Weapons.AddNew();
            w.EditorID = "SyntheticWeapon" + i;
        }
        mod.WriteToBinary(Path.Combine(dir, modKeyName));
        return dir;
    }
}

public sealed class RawScannerTests
{
    [Fact]
    public void RawScan_CountsRecordsAndHeader()
    {
        var dir = SyntheticFixture.CreatePlugin();
        var path = Path.Combine(dir, "Synthetic.esp");
        var raw = RawPluginScanner.Scan(path);
        Assert.True(raw.TotalRecords == 3, "expected 3 records, got " + raw.TotalRecords);
        Assert.True(raw.Masters.Count == 0);
        Assert.False(raw.IsEsl);
        Assert.False(raw.IsEsm);
        Assert.NotEmpty(raw.Sha256);
    }
}

public sealed class PluginScannerTests
{
    [Fact]
    public void Scan_SyntheticPlugin_ReportsOwnRecordsInEslRange()
    {
        var dir = SyntheticFixture.CreatePlugin();
        var path = Path.Combine(dir, "Synthetic.esp");
        var raw = RawPluginScanner.Scan(path);
        var result = new PluginScanner().Scan(path, raw, "Synthetic.esp");
        Assert.True(result.ParsedByMutagen, result.ParseError);
        Assert.Equal(3, result.MutagenRecordCount);
        Assert.Equal(3, result.OwnRecordCount);
        Assert.Equal(0, result.OverrideRecordCount);
        Assert.Equal(0, result.OwnIdsBelow0x800);
        Assert.True(result.MinOwnFormId >= 0x800);
        Assert.True(result.MaxOwnFormId <= 0xFFF);
        Assert.True(result.EslSafe);
        Assert.False(result.RenumberRequired);
        Assert.False(result.SplitRequired);
        Assert.Empty(result.MasterKeysForTests());
    }
}

internal static class PluginScanResultTestExtensions
{
    public static System.Collections.Generic.IReadOnlyList<string> MasterKeysForTests(this PluginScanResult result)
        => result.Masters;
}

public sealed class ModFolderScannerTests
{
    [Fact]
    public void Scan_SyntheticFolder_FindsTargetPlugin()
    {
        var dir = SyntheticFixture.CreatePlugin();
        var result = new ModFolderScanner().Scan(new ScanOptions { ModFolder = dir });
        Assert.Single(result.Plugins);
        Assert.Equal("Synthetic.esp", result.TargetPluginFileName);
        Assert.NotEmpty(result.ModFolderHash);
        Assert.True(result.Plugins[0].IsTarget);
    }

    [Fact]
    public void Scan_MissingDataDirAndTarget_ReportsWarnings()
    {
        var dir = SyntheticFixture.CreatePlugin();
        var result = new ModFolderScanner().Scan(new ScanOptions
        {
            ModFolder = dir,
            DataDirectories = { Path.Combine(Path.GetTempPath(), "sms-missing-data-dir-" + Guid.NewGuid().ToString("N")) },
            TargetPlugin = "Nope.esp",
        });
        Assert.Contains(result.Warnings, w => w.Code == "DATA_DIR_NOT_FOUND");
        Assert.Contains(result.Warnings, w => w.Code == "TARGET_PLUGIN_NOT_FOUND");
    }
}

public sealed class RealCorpusTests
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
    public void SprawlingBimbos_Scan_MatchesGroundTruth()
    {
        var root = FindRepoRoot();
        if (root == null) return;
        var folder = Path.Combine(root, "test-corpus", "SprawlingBimbos");
        if (!Directory.Exists(folder)) return;
        var result = new ModFolderScanner().Scan(new ScanOptions { ModFolder = folder });
        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("SprawlingBimbos.esp", plugin.FileName);
        Assert.Equal(937, plugin.RawRecordCount);
        Assert.Equal(937, plugin.MutagenRecordCount);
        Assert.Equal(779, plugin.OwnRecordCount);
        Assert.Equal(158, plugin.OverrideRecordCount);
        Assert.Equal(0, plugin.OwnIdsBelow0x800);
        Assert.False(plugin.EslSafe);
        Assert.True(plugin.RenumberRequired);
        Assert.False(plugin.SplitRequired);
        Assert.Contains("SexLab.esm", plugin.Masters);
        Assert.Equal(49, result.Assets.FaceGenPairCount);
        Assert.Equal(0, result.Assets.FaceGenMissingPairCount);
        Assert.Equal(0, plugin.Links.Other);
        Assert.True(plugin.Links.Unresolved > 0);
        Assert.Equal(51, result.Scripts.Count(s => s.Kind == "pex"));
        Assert.Equal(47, result.Scripts.Count(s => s.Kind == "psc"));
        Assert.True(result.Assets.SeqCount >= 1);
        Assert.DoesNotContain(result.ConfigFiles, c => c.FileName.Equals("meta.ini", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.ConfigFiles, c => c.FileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CardWeapon_Scan_MatchesGroundTruthAndArchives()
    {
        var root = FindRepoRoot();
        if (root == null) return;
        var folder = Path.Combine(root, "test-corpus", "CardWeapon");
        if (!Directory.Exists(folder)) return;
        var result = new ModFolderScanner().Scan(new ScanOptions { ModFolder = folder });
        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("CardWeapon_SE.esp", plugin.FileName);
        Assert.Equal(3373, plugin.RawRecordCount);
        Assert.Equal(3373, plugin.MutagenRecordCount);
        Assert.Equal(3367, plugin.OwnRecordCount);
        Assert.Equal(6, plugin.OverrideRecordCount);
        Assert.Equal(0, plugin.OwnIdsBelow0x800);
        Assert.False(plugin.EslSafe);
        Assert.True(plugin.SplitRequired);
        Assert.Equal(0, plugin.Links.Other);
        Assert.True(plugin.Links.Unresolved > 0);
        Assert.Equal(2, result.Archives.Count);
        Assert.Contains(result.Archives, a => a.FilesCount == 1422);
        Assert.DoesNotContain(result.ConfigFiles, c => c.FileName.Equals("meta.ini", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ConfigFiles, c => c.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        var plan = new SkyrimModSplitter.Core.Conversion.ConversionPlanner().Plan(result, new SkyrimModSplitter.Core.Conversion.ConversionOptions { OutputFolder = Path.Combine(Path.GetTempPath(), "sms-cardweapon-plan") });
        Assert.Equal(SkyrimModSplitter.Core.Conversion.ConversionVerdict.SplitRequired, plan.Verdict);
        Assert.True(plan.IsBlocked);
    }
}

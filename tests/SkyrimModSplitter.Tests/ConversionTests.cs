using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Scanning;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class ConversionTests
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

    private static string CreateEslReadyPlugin()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sms-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("SyntheticReady.esp"), SkyrimRelease.SkyrimSE);
        var w1 = mod.Weapons.AddNew(); w1.EditorID = "ReadyWeapon1";
        var w2 = mod.Weapons.AddNew(); w2.EditorID = "ReadyWeapon2";
        var list = mod.FormLists.AddNew(); list.EditorID = "ReadyList";
        list.Items.Add(w1.ToLink());
        mod.WriteToBinary(Path.Combine(dir, "SyntheticReady.esp"));
        return dir;
    }

    [Fact]
    public void SyntheticEslReady_Conversion_SetsFlagWithoutRenumbering()
    {
        var dir = CreateEslReadyPlugin();
        var sourcePlugin = Path.Combine(dir, "SyntheticReady.esp");
        var sourceSha = Sha256(sourcePlugin);
        var scan = new ModFolderScanner().Scan(new ScanOptions { ModFolder = dir });
        var output = Path.Combine(Path.GetTempPath(), "sms-tests", "esl-ready-out", Guid.NewGuid().ToString("N"));
        var plan = new ConversionPlanner().Plan(scan, new ConversionOptions { OutputFolder = output, Overwrite = true });
        Assert.Equal(ConversionVerdict.EslReady, plan.Verdict);
        Assert.False(plan.NeedsRenumber);
        Assert.True(plan.NeedsEslFlag);
        Assert.Empty(plan.RecordMappings);

        var result = new EslConverter().Execute(plan, new ConversionOptions { OutputFolder = output, Overwrite = true });
        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.True(result.Validation.Passed, string.Join(" | ", result.Validation.Failures));

        var outPlugin = Path.Combine(output, "SyntheticReady.esp");
        var raw = RawPluginScanner.Scan(outPlugin);
        Assert.True(raw.IsEsl);
        Assert.Equal(3, raw.TotalRecords);
        var mod = SkyrimMod.CreateFromBinary(outPlugin, SkyrimRelease.SkyrimSE);
        Assert.True(mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Small));
        var ownIds = mod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey.Equals(mod.ModKey)).Select(r => r.FormKey.ID).OrderBy(id => id).ToList();
        Assert.Equal(new uint[] { 0x800, 0x801, 0x802 }, ownIds);
        Assert.Equal(0x800u, mod.FormLists.First().Items.First().FormKey.ID);

        Assert.Equal(sourceSha, Sha256(sourcePlugin));
        var sourceMod = SkyrimMod.CreateFromBinary(sourcePlugin, SkyrimRelease.SkyrimSE);
        Assert.False(sourceMod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Small));
    }

    [Fact]
    public void FaceGenRewriter_ReplacesOnlyFormIdFilename()
    {
        var path = Path.Combine("Meshes", "actors", "character", "FaceGenData", "FaceGeom", "Plugin.esp", "00000ABC.NIF");
        var replaced = FaceGenRewriter.ReplaceFormIdInPath(path, "00000ABC", "00000800");
        Assert.EndsWith(Path.Combine("Plugin.esp", "00000800.NIF"), replaced);
        var unchanged = FaceGenRewriter.ReplaceFormIdInPath(path, "00000FFF", "00000800");
        Assert.Equal(path, unchanged);
    }

    [Fact]
    public void SprawlingBimbos_RealConversion_ProducesValidEslOutput()
    {
        var root = FindRepoRoot();
        if (root == null) return;
        var source = Path.Combine(root, "test-corpus", "SprawlingBimbos");
        if (!Directory.Exists(source)) return;
        var dataDirs = new[] {
            @"H:\Skyrim\MantrasOfMara\Stock Game\Data",
            @"H:\Skyrim\MantrasOfMara\mods\SexLab Framework",
            @"H:\Skyrim\MantrasOfMara\mods\SLO Aroused NG",
            @"H:\Skyrim\MantrasOfMara\mods\[NoDelete]_SLGP_BoS - Bimbos of Skyrim",
        }.Where(Directory.Exists).ToList();
        var scan = new ModFolderScanner().Scan(new ScanOptions { ModFolder = source, DataDirectories = dataDirs });
        var output = Path.Combine(Path.GetTempPath(), "sms-tests", "slice2-real-" + Guid.NewGuid().ToString("N"));
        var plan = new ConversionPlanner().Plan(scan, new ConversionOptions { OutputFolder = output, Overwrite = true });
        Assert.Equal(ConversionVerdict.EslRenumber, plan.Verdict);
        Assert.Equal(779, plan.RecordMappings.Count);
        Assert.True(plan.AssetRenames.Count >= 98, "expected FaceGen asset renames, got " + plan.AssetRenames.Count);

        var sourceSha = Sha256(Path.Combine(source, "SprawlingBimbos.esp"));
        var result = new EslConverter().Execute(plan, new ConversionOptions { OutputFolder = output, Overwrite = true });
        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.True(result.Validation.Passed, string.Join(" | ", result.Validation.Failures));
        Assert.Equal(sourceSha, Sha256(Path.Combine(source, "SprawlingBimbos.esp")));

        var converted = Path.Combine(output, "SprawlingBimbos.esp");
        var raw = RawPluginScanner.Scan(converted);
        var sourceRaw = RawPluginScanner.Scan(Path.Combine(source, "SprawlingBimbos.esp"));
        Assert.True(raw.IsEsl);
        Assert.Equal(937, raw.TotalRecords);
        Assert.Equal(sourceRaw.Masters, raw.Masters);
        Assert.Equal(sourceRaw.NumRecordsStated, raw.NumRecordsStated);
        Assert.Equal(8, raw.Masters.Count);
        var outputModHeader = SkyrimMod.CreateFromBinary(converted, SkyrimRelease.SkyrimSE).ModHeader;
        Assert.Equal(0xB0Bu, outputModHeader.Stats.NextFormID);
        var rescan = new ModFolderScanner().Scan(new ScanOptions { ModFolder = output, DataDirectories = dataDirs });
        var plugin = Assert.Single(rescan.Plugins);
        Assert.True(plugin.EslSafe);
        Assert.Equal(779, plugin.OwnRecordCount);
        Assert.Equal(158, plugin.OverrideRecordCount);
        Assert.Equal(0, plugin.OwnIdsBelow0x800);
        Assert.True(plugin.MinOwnFormId >= 0x800);
        Assert.True(plugin.MaxOwnFormId <= 0xFFF);

        var alreadyOutput = Path.Combine(Path.GetTempPath(), "sms-tests", "already-light-" + Guid.NewGuid().ToString("N"));
        var plan2 = new ConversionPlanner().Plan(rescan, new ConversionOptions { OutputFolder = alreadyOutput });
        Assert.Equal(ConversionVerdict.AlreadyLight, plan2.Verdict);
        var alreadyResult = new EslConverter().Execute(plan2, new ConversionOptions { OutputFolder = alreadyOutput });
        Assert.False(alreadyResult.Success);
        Assert.Contains(alreadyResult.Errors, e => e.Contains("already ESL", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(alreadyOutput));

        var badPlan = new ConversionPlan
        {
            SourcePluginPath = plan.SourcePluginPath,
            SourceMasters = new System.Collections.Generic.List<string> { "Skyrim.esm" },
            SourceStatedRecordCount = plan.SourceStatedRecordCount,
            RawRecordCount = plan.RawRecordCount,
            OwnRecordCount = plan.OwnRecordCount,
            OverrideRecordCount = plan.OverrideRecordCount,
            NeedsRenumber = plan.NeedsRenumber,
            RecordMappings = plan.RecordMappings,
            EslBudget = plan.EslBudget,
            SourcePluginFileName = plan.SourcePluginFileName,
        };
        var badValidation = ConverterValidator.Validate(converted, badPlan);
        Assert.False(badValidation.Passed);
        Assert.Contains(badValidation.Failures, f => f.Contains("master list changed"));

        foreach (var rename in plan.AssetRenames)
            Assert.True(File.Exists(Path.Combine(output, rename.NewRelativePath)), "missing renamed asset: " + rename.NewRelativePath);

        Assert.True(File.Exists(Path.Combine(output, "conversion-manifest.json")));
        Assert.True(File.Exists(Path.Combine(output, "CONVERSION-REPORT.md")));
        using (var manifestDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "conversion-manifest.json"))))
        {
            var outputNode = manifestDoc.RootElement.GetProperty("output");
            var manifestPluginPath = outputNode.GetProperty("outputPluginPath").GetString();
            var manifestPluginSha = outputNode.GetProperty("outputPluginSha256").GetString();
            Assert.False(string.IsNullOrWhiteSpace(manifestPluginPath));
            Assert.Equal(Sha256(converted), manifestPluginSha);
        }

        // Slice 3 content-rewrite reporting
        Assert.True(result.Coverage.Passed);
        Assert.Equal(0, result.Coverage.StaleOldTokens);
        Assert.True(result.Papyrus.PscScanned >= 47, "psc scanned: " + result.Papyrus.PscScanned);
        Assert.Equal(1, result.Seq.SeqFiles);
        Assert.True(result.Seq.DialogueRenumbered);
        Assert.Contains(result.Warnings, w => w.Code == "SEQ_NOT_REGENERATED");
        Assert.True(result.ConfigRewrite.FilesScanned >= 0);
    }

    [Fact]
    public void AncestorOutputFolder_IsRefusedWithoutDeletingSource()
    {
        var sourceDir = SyntheticFixture.CreatePlugin();
        var sourcePlugin = Path.Combine(sourceDir, "Synthetic.esp");
        var scan = new ModFolderScanner().Scan(new ScanOptions { ModFolder = sourceDir });
        var ancestor = Path.GetDirectoryName(sourceDir)!;
        var plan = new ConversionPlanner().Plan(scan, new ConversionOptions { OutputFolder = ancestor, Overwrite = true });
        Assert.Equal(ConversionVerdict.EslReady, plan.Verdict);
        var result = new EslConverter().Execute(plan, new ConversionOptions { OutputFolder = ancestor, Overwrite = true });
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("ancestor", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(sourcePlugin));
        Assert.Single(new ModFolderScanner().Scan(new ScanOptions { ModFolder = sourceDir }).Plugins);
    }
}

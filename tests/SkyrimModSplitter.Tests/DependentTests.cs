using System;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SkyrimModSplitter.Core.Dependents;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class DependentTests
{
    [Fact]
    public void ScanAndPatch_DependentPluginAndConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sms-dependents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var targetKey = ModKey.FromNameAndExtension("Target.esp");
        var patchKey = ModKey.FromNameAndExtension("Patch.esp");
        var target = new SkyrimMod(targetKey, SkyrimRelease.SkyrimSE);
        var weapon = target.Weapons.AddNew();
        weapon.EditorID = "TargetWeapon";
        target.WriteToBinary(Path.Combine(dir, "Target.esp"));

        var patch = new SkyrimMod(patchKey, SkyrimRelease.SkyrimSE);
        patch.ModHeader.MasterReferences.Add(new MasterReference { Master = targetKey });
        var list = patch.FormLists.AddNew();
        list.EditorID = "PatchList";
        list.Items.Add(new FormLink<IItemGetter>(new FormKey(targetKey, 0x800)));
        patch.WriteToBinary(Path.Combine(dir, "Patch.esp"));
        File.WriteAllText(Path.Combine(dir, "patch.ini"), "link = 0x000800~Target.esp\n");

        var scan = DependentScanner.Scan(new DependentScanOptions
        {
            TargetPluginFileName = "Target.esp",
            SearchRoot = dir,
        });
        Assert.True(scan.Checked);
        Assert.Single(scan.Plugins);
        Assert.Single(scan.Configs);

        var output = Path.Combine(dir, "dependents-out");
        var patchResult = DependentPatcher.Patch(scan, new DependentPatchOptions
        {
            OutputFolder = output,
            Remap = new System.Collections.Generic.List<IdRemapEntry>
            {
                new IdRemapEntry { SourcePlugin = "Target.esp", OldId = 0x800, NewPlugin = "Target.esp", NewId = 0x810 },
            },
        });
        Assert.Empty(patchResult.Errors);
        Assert.Equal(2, patchResult.WrittenFiles.Count);
        var patchedPlugin = SkyrimMod.CreateFromBinary(Path.Combine(output, "plugins", "Patch.esp"), SkyrimRelease.SkyrimSE);
        Assert.Equal(0x810u, patchedPlugin.FormLists.First().Items.First().FormKey.ID);
        var patchedConfig = File.ReadAllText(Path.Combine(output, "configs", "patch.ini"));
        Assert.Contains("0x810~Target.esp", patchedConfig);
    }
}

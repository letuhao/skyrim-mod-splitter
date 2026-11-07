using System;
using System.IO;
using System.Linq;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Rewriting;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class PapyrusTests
{
    private static ConversionPlan Plan(uint oldId, uint newId)
    {
        var plan = new ConversionPlan { SourcePluginFileName = "Old.esp", SourceFolder = @"C:\x", OutputFolder = @"C:\y" };
        plan.RecordMappings.Add(new RecordIdMapping
        {
            RecordType = "MagicEffect",
            OldObjectId = oldId,
            NewObjectId = newId,
            OldFormKey = "Old.esp|0x" + oldId.ToString("X"),
            NewFormKey = "Old.esp|0x" + newId.ToString("X"),
        });
        return plan;
    }

    [Fact]
    public void ResolveDeployedPexPath_MapsSourceFolderToScriptsTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sms-papyrus", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(dir, "scripts", "Source");
        var deployedDir = Path.Combine(dir, "scripts");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deployedDir);
        var psc = Path.Combine(sourceDir, "SMSProbe.psc");
        File.WriteAllText(psc, "x = 0\n");
        var deployed = Path.Combine(deployedDir, "SMSProbe.pex");
        File.WriteAllBytes(deployed, new byte[] { 1, 2, 3 });
        var resolved = PapyrusRewriter.ResolveDeployedPexPath(dir, psc);
        Assert.Equal(Path.GetFullPath(deployed), Path.GetFullPath(resolved));
    }

    [Fact]
    public void CompileFailure_RestoresSourceAndLeavesDeployedPex()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sms-papyrus", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(dir, "scripts", "Source");
        var deployedDir = Path.Combine(dir, "scripts");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deployedDir);
        var psc = Path.Combine(sourceDir, "SMSProbe.psc");
        var originalText = "x = 0x00000D62\n";
        File.WriteAllText(psc, originalText);
        var deployed = Path.Combine(deployedDir, "SMSProbe.pex");
        var placeholder = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(deployed, placeholder);
        var flags = Path.Combine(dir, "flags.flg");
        File.WriteAllText(flags, "");
        var previousCompiler = Environment.GetEnvironmentVariable("SMS_PAPYRUS_COMPILER");
        var previousFlags = Environment.GetEnvironmentVariable("SMS_PAPYRUS_FLAGS");
        Environment.SetEnvironmentVariable("SMS_PAPYRUS_COMPILER", @"C:\Windows\System32\cmd.exe");
        Environment.SetEnvironmentVariable("SMS_PAPYRUS_FLAGS", flags);
        try
        {
            var summary = PapyrusRewriter.Rewrite(dir, Plan(0x000D62, 0x811));
            Assert.Equal(1, summary.PscModified);
            Assert.Contains(summary.Warnings, w => w.Code == "PAPYRUS_COMPILE_FAILED");
            Assert.Contains("0x00000D62", File.ReadAllText(psc));
            Assert.Equal(placeholder, File.ReadAllBytes(deployed));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMS_PAPYRUS_COMPILER", previousCompiler);
            Environment.SetEnvironmentVariable("SMS_PAPYRUS_FLAGS", previousFlags);
        }
    }
}

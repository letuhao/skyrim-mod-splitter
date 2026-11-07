using System;
using System.IO;
using System.Linq;
using System.Text;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Rewriting;
using Xunit;

namespace SkyrimModSplitter.Tests;

public sealed class RewritingTests
{
    private static ConversionPlan PlanWithMappings()
    {
        var plan = new ConversionPlan
        {
            SourcePluginFileName = "Old.esp",
            SourceFolder = @"C:\none",
            OutputFolder = @"C:\none-out",
        };
        plan.RecordMappings.Add(new RecordIdMapping { RecordType = "Spell", OldObjectId = 0x01A2B3, NewObjectId = 0x810, OldFormKey = "Old.esp|0x01A2B3", NewFormKey = "Old.esp|0x810" });
        plan.RecordMappings.Add(new RecordIdMapping { RecordType = "DialogResponses", OldObjectId = 0x000D62, NewObjectId = 0x811, OldFormKey = "Old.esp|0x000D62", NewFormKey = "Old.esp|0x811" });
        return plan;
    }

    private static string NewDir() => Path.Combine(Path.GetTempPath(), "sms-rewrite", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConfigRewriter_RewritesSelfTokensOnly_AndReportsXedit()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.ini"), "spell = 0x0001A2B3~Old.esp\nbare = 0x00000D62\nexternal = 0x123456~Other.esp\n");
        File.WriteAllText(Path.Combine(dir, "tool.pas"), "x = 0x0001A2B3;\n");
        var summary = ConfigRewriter.Rewrite(dir, PlanWithMappings());
        Assert.Equal(2, summary.Edits.Count);
        Assert.Equal(1, summary.ExternalTokens);
        var text = File.ReadAllText(Path.Combine(dir, "config.ini"));
        Assert.Contains("0x810", text);
        Assert.Contains("0x811", text);
        Assert.Contains("0x123456~Other.esp", text);
        Assert.Contains(summary.Warnings, w => w.Code == "XEDIT_SCRIPT_REPORT_ONLY");
    }

    [Fact]
    public void ConfigRewriter_PreservesUtf16AndLatin1Encodings()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        var utf16Path = Path.Combine(dir, "utf16.ini");
        var utf16Body = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("value = 0x00000D62\r\n")).ToArray();
        File.WriteAllBytes(utf16Path, utf16Body);
        var latin1Path = Path.Combine(dir, "latin1.ini");
        File.WriteAllBytes(latin1Path, Encoding.Latin1.GetBytes("cafe\u00e9 = 0x00000D62\n"));
        ConfigRewriter.Rewrite(dir, PlanWithMappings());
        var utf16After = File.ReadAllBytes(utf16Path);
        Assert.True(utf16After.Length >= 2 && utf16After[0] == 0xFF && utf16After[1] == 0xFE, "UTF-16 BOM lost");
        Assert.Contains("0x811", Encoding.Unicode.GetString(utf16After, 2, utf16After.Length - 2));
        var latin1After = File.ReadAllBytes(latin1Path);
        Assert.Contains((byte)0xE9, latin1After);
        Assert.Contains("0x811", Encoding.Latin1.GetString(latin1After));
    }

    [Fact]
    public void ConfigRewriter_HandlesApostrophePluginAndRejectsLongHex()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        var config = Path.Combine(dir, "config.ini");
        File.WriteAllText(config, "ext = 0x00000D62~Dragon's Keep.esp\nlong = 0x0004C7340\nself = 0x00000D62~Old.esp\n");
        ConfigRewriter.Rewrite(dir, PlanWithMappings());
        var text = File.ReadAllText(config);
        Assert.Contains("0x00000D62~Dragon's Keep.esp", text);
        Assert.Contains("0x0004C7340", text);
        Assert.Contains("0x811~Old.esp", text);
    }

    [Fact]
    public void CoverageScanner_IgnoresExternalXmlAndMetadata()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "meta.ini"), "x = 0x00000D62\n");
        File.WriteAllText(Path.Combine(dir, "page.xml"), "x = 0x00000D62\n");
        File.WriteAllText(Path.Combine(dir, "external.ini"), "x = 0x00000D62~Dragon's Keep.esp\n");
        var summary = CoverageScanner.Scan(dir, PlanWithMappings());
        Assert.Equal(0, summary.StaleOldTokens);
        Assert.True(summary.Passed);
    }

    [Fact]
    public void VoiceSeqRewriter_RenamesVoiceIds_AndWarnsOnSeq()
    {
        var dir = NewDir();
        var voiceDir = Path.Combine(dir, "Sound", "Voice", "Old.esp", "Voice");
        var seqDir = Path.Combine(dir, "Seq");
        Directory.CreateDirectory(voiceDir);
        Directory.CreateDirectory(seqDir);
        File.WriteAllBytes(Path.Combine(voiceDir, "00000D62_1.fuz"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(seqDir, "Old.seq"), Array.Empty<byte>());
        var warnings = new System.Collections.Generic.List<SkyrimModSplitter.Core.Scanning.WarningItem>();
        var result = VoiceSeqRewriter.Rewrite(dir, PlanWithMappings(), warnings);
        Assert.Single(result.Renames);
        Assert.True(File.Exists(Path.Combine(voiceDir, "00000811_1.fuz")));
        Assert.False(File.Exists(Path.Combine(voiceDir, "00000D62_1.fuz")));
        Assert.Equal(1, result.Seq.SeqFiles);
        Assert.True(result.Seq.DialogueRenumbered);
        Assert.Contains(warnings, w => w.Code == "SEQ_NOT_REGENERATED");
    }

    [Fact]
    public void CoverageScanner_DetectsStaleOldTokens()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stale.ini"), "value = 0x0001A2B3\n");
        var plan = PlanWithMappings();
        var bad = CoverageScanner.Scan(dir, plan);
        Assert.Equal(1, bad.StaleOldTokens);
        Assert.False(bad.Passed);
        File.WriteAllText(Path.Combine(dir, "stale.ini"), "value = 0x810\n");
        var good = CoverageScanner.Scan(dir, plan);
        Assert.Equal(0, good.StaleOldTokens);
        Assert.True(good.Passed);
    }
}

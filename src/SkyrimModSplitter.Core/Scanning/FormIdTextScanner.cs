using System.Text.RegularExpressions;

namespace SkyrimModSplitter.Core.Scanning;

public static class FormIdTextScanner
{
    private static readonly Regex HexToken = new(@"0x([0-9A-Fa-f]{1,8})\b", RegexOptions.Compiled);
    private static readonly Regex PluginToken = new(@"[A-Za-z0-9_\-\.']+\.(?:esp|esm|esl)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const long MaxScanBytes = 4L * 1024 * 1024;
    private const int MaxSamples = 25;

    public static ConfigFileScan Scan(string relativePath, string fullPath)
    {
        var info = new ConfigFileScan
        {
            FileName = Path.GetFileName(fullPath),
            RelativePath = relativePath,
            SizeBytes = new FileInfo(fullPath).Length,
            Format = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant(),
        };
        if (info.SizeBytes > MaxScanBytes)
        {
            info.Error = "file larger than 4MB; skipped in dry-run scan";
            return info;
        }
        try
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(fullPath))
            {
                lineNumber++;
                foreach (Match m in HexToken.Matches(line))
                {
                    info.FormIdTokenCount++;
                    if (info.FormIdSamples.Count < MaxSamples)
                        info.FormIdSamples.Add(new FormIdTokenSample { LineNumber = lineNumber, Token = m.Value });
                }
                foreach (Match m in PluginToken.Matches(line))
                {
                    info.PluginNameTokenCount++;
                    if (info.PluginNameSamples.Count < MaxSamples)
                        info.PluginNameSamples.Add(new ConfigPluginReference { LineNumber = lineNumber, Token = m.Value });
                }
            }
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }
        return info;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using SkyrimModSplitter.Core.Tools;

namespace SkyrimModSplitter.Core.Scanning;

public static class BSArchService
{
    private static readonly Regex FilesRegex = new(@"^\s*Files:\s*(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex FormatRegex = new(@"^\s*Format:\s*(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"^\s*Version:\s*(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex FlagsRegex = new(@"^\s*Archive Flags:\s*(.+?)\s*$", RegexOptions.Compiled);

    public static ArchiveInfo List(string archivePath, string? bsArchPath)
    {
        var info = new ArchiveInfo
        {
            FileName = Path.GetFileName(archivePath),
            RelativePath = archivePath,
            SizeBytes = new FileInfo(archivePath).Length,
        };
        if (bsArchPath == null || !File.Exists(bsArchPath))
        {
            info.Error = "BSArch not found; archive contents were not listed";
            return info;
        }
        var result = ExternalToolRunner.Run(bsArchPath, new[] { archivePath, "-list" }, 180000);
        info.ExitCode = result.ExitCode;
        if (result.TimedOut) { info.Error = "BSArch timed out"; return info; }
        if (result.ExitCode != 0)
        {
            info.Error = "BSArch exit " + result.ExitCode + ": " + Trim(result.StdErr);
            return info;
        }
        foreach (var line in result.StdOut.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var m = FilesRegex.Match(trimmed);
            if (m.Success) { info.FilesCount = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            m = FormatRegex.Match(trimmed);
            if (m.Success) { info.Format = m.Groups[1].Value; continue; }
            m = VersionRegex.Match(trimmed);
            if (m.Success) { info.Version = m.Groups[1].Value; continue; }
            m = FlagsRegex.Match(trimmed);
            if (m.Success) { info.ArchiveFlags = m.Groups[1].Value; }
        }
        info.Parsed = info.FilesCount > 0;
        if (!info.Parsed && string.IsNullOrEmpty(info.Error)) info.Error = "could not parse BSArch -list output";
        return info;
    }

    private static string Trim(string s) => s.Length <= 300 ? s.Trim() : s[..300].Trim();
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyrimModSplitter.Core.Rewriting;

/// <summary>
/// Single source of truth for FormID token syntax shared by the rewriter and the
/// coverage scanner, so they cannot disagree about what was rewritable.
/// </summary>
public static class RewriteRules
{
    public static readonly Regex FormIdToken = new(
        @"(?<!0x)0x(?<hex>[0-9A-Fa-f]{1,8})(?![0-9A-Fa-f])(?:~(?<plugin>[^~|,\r\n]*?\.(?:esp|esm|esl)))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ini", ".json", ".txt" };
    public static readonly HashSet<string> ReportOnlyExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pas" };

    public static bool IsMetadataFile(string fileName)
        => fileName.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    public static bool IsSelfPlugin(string? plugin, string sourcePluginFileName)
    {
        if (string.IsNullOrWhiteSpace(plugin)) return true;
        var trimmed = plugin.Trim();
        return trimmed.Equals(sourcePluginFileName, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(Path.GetFileNameWithoutExtension(sourcePluginFileName), StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(Match match, out uint objectId, out string? plugin)
    {
        objectId = 0;
        plugin = null;
        if (!uint.TryParse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out objectId)) return false;
        if (match.Groups["plugin"].Success)
        {
            var value = match.Groups["plugin"].Value.Trim();
            if (value.Length > 0) plugin = value;
        }
        return true;
    }
}

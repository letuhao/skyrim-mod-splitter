using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Scanning;
using SkyrimModSplitter.Core.Tools;

namespace SkyrimModSplitter.Core.Rewriting;

public static class PapyrusRewriter
{
    private static readonly Regex HexToken = new(@"0x(?<hex>[0-9A-Fa-f]{1,8})(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private const int CompileTimeoutMs = 240000;

    public static PapyrusSummary Rewrite(string stagingRoot, ConversionPlan plan)
    {
        var summary = new PapyrusSummary();
        var map = plan.RecordMappings.ToDictionary(m => m.OldObjectId, m => m.NewObjectId);
        if (map.Count == 0) return summary;
        var compiler = ToolLocator.FindPapyrusCompiler();
        var flags = ToolLocator.FindPapyrusFlags();
        var champollion = ToolLocator.FindChampollion();
        var pscFiles = Directory.EnumerateFiles(stagingRoot, "*.psc", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var importDirs = BuildImportDirs(stagingRoot, pscFiles);
        foreach (var psc in pscFiles)
        {
            summary.PscScanned++;
            ProcessPsc(stagingRoot, psc, map, compiler, flags, importDirs, summary);
        }
        if (champollion != null)
        {
            var pscNames = new HashSet<string>(pscFiles.Select(Path.GetFileNameWithoutExtension)!, StringComparer.OrdinalIgnoreCase);
            foreach (var pex in Directory.EnumerateFiles(stagingRoot, "*.pex", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (pscNames.Contains(Path.GetFileNameWithoutExtension(pex))) continue;
                summary.PexScanned++;
                ProcessPexOnly(pex, map, champollion, compiler, flags, importDirs, summary);
            }
        }
        else if (Directory.EnumerateFiles(stagingRoot, "*.pex", SearchOption.AllDirectories).Any())
        {
            summary.Warnings.Add(new WarningItem { Code = "CHAMPOLLION_MISSING", Severity = Severity.Warning, Message = "Champollion not found; pex-only scripts were not decompiled or checked for literal FormIDs" });
        }
        return summary;
    }

    /// <summary>
    /// Compiles live beside the .psc, which can be in a Source folder while the
    /// deployed .pex sits in the scripts tree. Resolve the deployed location.
    /// </summary>
    public static string ResolveDeployedPexPath(string stagingRoot, string pscPath)
    {
        var scriptName = Path.GetFileNameWithoutExtension(pscPath);
        var relative = Path.GetRelativePath(stagingRoot, pscPath);
        var directory = Path.GetDirectoryName(relative) ?? "";
        var segments = directory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.Equals("source", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var targetDir = Path.Combine(new[] { stagingRoot }.Concat(segments).ToArray());
        var derived = Path.Combine(targetDir, scriptName + ".pex");
        if (File.Exists(derived)) return derived;
        var existing = Directory.EnumerateFiles(stagingRoot, scriptName + ".pex", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s.Equals("source", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Length)
            .FirstOrDefault();
        return existing ?? derived;
    }

    private static void ProcessPsc(string stagingRoot, string psc, IReadOnlyDictionary<uint, uint> map, string? compiler, string? flags, IReadOnlyList<string> importDirs, PapyrusSummary summary)
    {
        try
        {
            var original = File.ReadAllText(psc);
            var rewritten = RewriteText(original, map, out var hits);
            if (hits == 0) return;
            var result = new PapyrusScriptResult
            {
                RelativePath = Path.GetFileName(psc),
                Mode = "psc",
                LiteralTokens = hits,
                RewrittenTokens = hits,
                DeployedPexPath = Path.GetRelativePath(stagingRoot, ResolveDeployedPexPath(stagingRoot, psc)),
            };
            summary.Scripts.Add(result);
            summary.PscModified++;
            File.WriteAllText(psc, rewritten, new UTF8Encoding(false));
            if (compiler == null || flags == null)
            {
                summary.Warnings.Add(new WarningItem { Code = "PAPYRUS_COMPILER_MISSING", Severity = Severity.Warning, Message = "literal FormIDs rewritten in " + Path.GetFileName(psc) + " but PapyrusCompiler/flags not found; pex not rebuilt" });
                return;
            }
            var compile = Compile(compiler, flags, importDirs, psc);
            try
            {
                result.Compiled = compile.Success;
                result.CompileOutput = Trim(compile.Output);
                if (compile.Success && compile.CompiledPex != null)
                {
                    var deployed = ResolveDeployedPexPath(stagingRoot, psc);
                    Directory.CreateDirectory(Path.GetDirectoryName(deployed)!);
                    File.Copy(compile.CompiledPex, deployed, overwrite: true);
                }
                else
                {
                    File.WriteAllText(psc, original, new UTF8Encoding(false));
                    summary.Warnings.Add(new WarningItem { Code = "PAPYRUS_COMPILE_FAILED", Severity = Severity.Warning, Message = "recompile failed for " + Path.GetFileName(psc) + "; source restored, deployed pex left unchanged. " + Trim(compile.Output) });
                }
            }
            finally
            {
                TryDeleteDirectory(compile.OutputDir);
            }
        }
        catch (Exception ex)
        {
            summary.Warnings.Add(new WarningItem { Code = "PAPYRUS_PSC_FAILED", Severity = Severity.Warning, Message = Path.GetFileName(psc) + ": " + ex.Message });
        }
    }

    private static void ProcessPexOnly(string pex, IReadOnlyDictionary<uint, uint> map, string champollion, string? compiler, string? flags, IReadOnlyList<string> importDirs, PapyrusSummary summary)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sms-pex-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var decompile = ExternalToolRunner.Run(champollion, new[] { "-p", tempDir, "-c", pex }, 180000);
            if (decompile.ExitCode != 0 || decompile.TimedOut)
            {
                summary.Warnings.Add(new WarningItem { Code = "PEX_DECOMPILE_FAILED", Severity = Severity.Warning, Message = Path.GetFileName(pex) + ": " + Trim(decompile.StdErr) });
                return;
            }
            var scriptName = Path.GetFileNameWithoutExtension(pex);
            var decompiled = Directory.EnumerateFiles(tempDir, scriptName + ".psc", SearchOption.AllDirectories).FirstOrDefault();
            if (decompiled == null)
            {
                summary.Warnings.Add(new WarningItem { Code = "PEX_DECOMPILE_NO_OUTPUT", Severity = Severity.Warning, Message = Path.GetFileName(pex) + ": Champollion produced no psc" });
                return;
            }
            summary.PexDecompiled++;
            var original = File.ReadAllText(decompiled);
            var rewritten = RewriteText(original, map, out var hits);
            var result = new PapyrusScriptResult { RelativePath = scriptName + ".pex", Mode = "pex", LiteralTokens = hits, RewrittenTokens = hits, DeployedPexPath = Path.GetFileName(pex) };
            summary.Scripts.Add(result);
            if (hits == 0) return;
            summary.PexLiteralTokens += hits;
            summary.Warnings.Add(new WarningItem { Code = "PEX_ONLY_LITERAL_FORMIDS", Severity = Severity.Warning, Message = Path.GetFileName(pex) + " has " + hits + " mapped literal FormID token(s); decompiled and rewritten best-effort" });
            if (compiler == null || flags == null) return;
            var rewrittenPath = Path.Combine(tempDir, scriptName + ".psc");
            File.WriteAllText(rewrittenPath, rewritten, new UTF8Encoding(false));
            var compile = Compile(compiler, flags, importDirs, rewrittenPath);
            try
            {
                result.Compiled = compile.Success;
                result.CompileOutput = Trim(compile.Output);
                if (compile.Success && compile.CompiledPex != null)
                    File.Copy(compile.CompiledPex, pex, overwrite: true);
                else
                    summary.Warnings.Add(new WarningItem { Code = "PAPYRUS_COMPILE_FAILED", Severity = Severity.Warning, Message = "pex-only recompile failed for " + Path.GetFileName(pex) + ": " + Trim(compile.Output) });
            }
            finally
            {
                TryDeleteDirectory(compile.OutputDir);
            }
        }
        catch (Exception ex)
        {
            summary.Warnings.Add(new WarningItem { Code = "PEX_REWRITE_FAILED", Severity = Severity.Warning, Message = Path.GetFileName(pex) + ": " + ex.Message });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string RewriteText(string text, IReadOnlyDictionary<uint, uint> map, out int hits)
    {
        var count = 0;
        var rewritten = HexToken.Replace(text, match =>
        {
            if (!uint.TryParse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) return match.Value;
            if (!map.TryGetValue(id, out var newId)) return match.Value;
            count++;
            return "0x" + newId.ToString("X");
        });
        hits = count;
        return rewritten;
    }

    private static (bool Success, string? CompiledPex, string OutputDir, string Output) Compile(string compiler, string flags, IReadOnlyList<string> importDirs, string scriptPath)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "sms-pex-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var args = new List<string>
        {
            scriptPath,
            "-f=" + flags,
            "-i=" + string.Join(";", importDirs),
            "-o=" + outDir,
        };
        var result = ExternalToolRunner.Run(compiler, args, CompileTimeoutMs);
        var scriptName = Path.GetFileNameWithoutExtension(scriptPath);
        var compiled = Directory.EnumerateFiles(outDir, scriptName + ".pex", SearchOption.AllDirectories).FirstOrDefault();
        var output = (result.StdOut + " " + result.StdErr).Trim();
        return (result.ExitCode == 0 && !result.TimedOut && compiled != null, compiled, outDir, output);
    }

    private static List<string> BuildImportDirs(string stagingRoot, List<string> pscFiles)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var psc in pscFiles)
        {
            var dir = Path.GetDirectoryName(psc);
            if (dir != null) dirs.Add(dir);
        }
        foreach (var dir in ToolLocator.FindVanillaScriptDirs()) dirs.Add(dir);
        return dirs.ToList();
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private static string Trim(string s) => s.Length <= 400 ? s.Trim() : s[..400].Trim();
}

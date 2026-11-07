using System.Diagnostics;

namespace SkyrimModSplitter.Core.Tools;

public sealed record ToolResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

public static class ExternalToolRunner
{
    public static ToolResult Run(string exePath, IEnumerable<string> arguments, int timeoutMs = 120000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start " + exePath);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ToolResult(-1, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult(), true);
        }
        return new ToolResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult(), false);
    }
}

public static class ToolLocator
{
    public const string DefaultBSArchPath =
        @"D:\Works\source\skyrim-moddings\_tools\BSArch-64745-0-9-1660562025\sse\BSArch.exe";
    public const string DefaultChampollionPath =
        @"D:\Works\source\skyrim-moddings\_tools\Champollion\Champollion.exe";

    private static readonly string[] PapyrusCompilerCandidates =
    {
        @"H:\Skyrim\TwistedSkyrim\Stock Game\Papyrus Compiler\PapyrusCompiler.exe",
        @"E:\SteamLibrary\steamapps\common\Skyrim Special Edition\Papyrus Compiler\PapyrusCompiler.exe",
    };

    private static readonly string[] PapyrusFlagsCandidates =
    {
        @"H:\Skyrim\TwistedSkyrim\Stock Game\Data\Source\Scripts\TESV_Papyrus_Flags.flg",
        @"E:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data\Source\Scripts\TESV_Papyrus_Flags.flg",
    };

    private static readonly string[] VanillaScriptDirCandidates =
    {
        @"H:\Skyrim\TwistedSkyrim\Stock Game\Data\Source\Scripts",
        @"H:\Skyrim\TwistedSkyrim\Stock Game\Data\Scripts\Source",
        @"E:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data\Source\Scripts",
        @"E:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data\Scripts\Source",
        @"H:\Skyrim\MantrasOfMara\Stock Game\Data\Source\Scripts",
        @"H:\Skyrim\MantrasOfMara\Stock Game\Data\Scripts\Source",
    };

    public static string? FindBSArch(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;
        var env = Environment.GetEnvironmentVariable("SMS_BSARCH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        return File.Exists(DefaultBSArchPath) ? DefaultBSArchPath : null;
    }

    public static string? FindChampollion()
    {
        var env = Environment.GetEnvironmentVariable("SMS_CHAMPOLLION");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        return File.Exists(DefaultChampollionPath) ? DefaultChampollionPath : null;
    }

    public static string? FindPapyrusCompiler()
    {
        var env = Environment.GetEnvironmentVariable("SMS_PAPYRUS_COMPILER");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        return PapyrusCompilerCandidates.FirstOrDefault(File.Exists);
    }

    public static string? FindPapyrusFlags()
    {
        var env = Environment.GetEnvironmentVariable("SMS_PAPYRUS_FLAGS");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        return PapyrusFlagsCandidates.FirstOrDefault(File.Exists);
    }

    public static IReadOnlyList<string> FindVanillaScriptDirs()
    {
        var env = Environment.GetEnvironmentVariable("SMS_VANILLA_SCRIPTS");
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(env))
            dirs.AddRange(env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists));
        dirs.AddRange(VanillaScriptDirCandidates.Where(Directory.Exists));
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

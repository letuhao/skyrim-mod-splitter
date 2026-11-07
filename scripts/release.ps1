param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $repo "SkyrimModSplitter.sln"))) { throw "run from the repository scripts folder" }
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
Push-Location $repo
try {
    & $dotnet build SkyrimModSplitter.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
    $stage = Join-Path $repo "artifacts\release\SkyrimModSplitter-$Version"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    $guiSource = Join-Path $repo "src\SkyrimModSplitter.Gui\bin\Release\net8.0-windows"
    $cliSource = Join-Path $repo "src\SkyrimModSplitter.Cli\bin\Release\net8.0"
    $guiDest = Join-Path $stage "gui"
    $cliDest = Join-Path $stage "cli"
    New-Item -ItemType Directory -Path $guiDest, $cliDest -Force | Out-Null
    Copy-Item -Path (Join-Path $guiSource "*") -Destination $guiDest -Recurse -Force -Exclude "*.pdb"
    Copy-Item -Path (Join-Path $cliSource "*") -Destination $cliDest -Recurse -Force -Exclude "*.pdb"
    Copy-Item -LiteralPath (Join-Path $repo "README.md") -Destination $stage -Force
    Copy-Item -LiteralPath (Join-Path $repo "LICENSE") -Destination $stage -Force
    New-Item -ItemType Directory -Path (Join-Path $stage "docs") -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo "docs\USAGE.md") -Destination (Join-Path $stage "docs") -Force
    Copy-Item -LiteralPath (Join-Path $repo "docs\LIMITATIONS.md") -Destination (Join-Path $stage "docs") -Force
    $notes = @"
Skyrim Mod Splitter $Version

GUI: gui\SkyrimModSplitter.Gui.exe
CLI: cli\SkyrimModSplitter.Cli.dll (dotnet cli\SkyrimModSplitter.Cli.dll ...)

Requires .NET 8 Desktop Runtime.
Structural validation only; in-game load testing is user-side.
"@
    Set-Content -LiteralPath (Join-Path $stage "RELEASE-NOTES.txt") -Value $notes -Encoding utf8NoBOM
    $zip = Join-Path $repo "artifacts\release\SkyrimModSplitter-$Version-win64.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
    $sha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ version = $Version; builtAtUtc = (Get-Date).ToUniversalTime().ToString("O"); zip = $zip; sha256 = $sha; gui = "gui\SkyrimModSplitter.Gui.exe"; cli = "cli\SkyrimModSplitter.Cli.dll" }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage "release-manifest.json") -Encoding utf8NoBOM
    Write-Output ("release zip: " + $zip)
    Write-Output ("sha256: " + $sha)
}
finally {
    Pop-Location
}

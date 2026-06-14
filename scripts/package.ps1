param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$IncludeSymbols
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'Jellyxtreme\Jellyxtreme.csproj'
$manifestPath = Join-Path $repoRoot 'Jellyxtreme\plugin.json'
$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
$version = if ($manifest.version) { [string]$manifest.version } else { '0.0.0' }
$dotnet = if (Test-Path -LiteralPath 'C:\dotnet9\dotnet.exe') {
    'C:\dotnet9\dotnet.exe'
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    'dotnet'
} else {
    throw 'Unable to find the .NET SDK. Install .NET 9 or add dotnet to PATH.'
}

& $dotnet build $projectPath --configuration $Configuration

$targetFramework = 'net9.0'
$outputDir = Join-Path $repoRoot "Jellyxtreme\bin\$Configuration\$targetFramework"
$artifactDir = Join-Path $repoRoot 'artifacts'
$stagingDir = Join-Path $artifactDir 'Jellyxtreme'
$zipPath = Join-Path $artifactDir "Jellyxtreme-$version.zip"

Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

Copy-Item -LiteralPath (Join-Path $outputDir 'Jellyxtreme.dll') -Destination $stagingDir
Copy-Item -LiteralPath (Join-Path $outputDir 'plugin.json') -Destination $stagingDir

if ($IncludeSymbols) {
    $pdbPath = Join-Path $outputDir 'Jellyxtreme.pdb'
    if (Test-Path -LiteralPath $pdbPath) {
        Copy-Item -LiteralPath $pdbPath -Destination $stagingDir
    }
}

Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $zipPath -Force
Write-Host "Created $zipPath"

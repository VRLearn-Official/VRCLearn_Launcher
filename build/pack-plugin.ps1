param(
    [Parameter(Mandatory = $true)]
    [string]$Project,

    [string]$Configuration = "Release",

    [string]$OutputDirectory = "artifacts/plugins"
)

$ErrorActionPreference = "Stop"

$projectPath = (Resolve-Path $Project).Path
$projectDir = Split-Path -Parent $projectPath
$manifestPath = Join-Path $projectDir "plugin.json"

if (-not (Test-Path $manifestPath)) {
    throw "plugin.json not found: $manifestPath"
}

$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$id = $manifest.id
$version = $manifest.version

if (-not $id -or -not $version) {
    throw "plugin.json must contain id and version"
}

$root = (& git -C $projectDir rev-parse --show-toplevel).Trim()

if ($LASTEXITCODE -ne 0 -or -not $root) {
    throw "could not resolve repository root from $projectDir"
}
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("vcl-plugin-" + [Guid]::NewGuid().ToString("N"))
$publishDir = Join-Path $staging "publish"
$packageDir = Join-Path $staging "package"

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

try {
    dotnet publish $projectPath `
        -c $Configuration `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed: $projectPath"
    }

    $hostAssemblies = @(
        "VRCLearn.Plugin.Sdk",
        "VRCLearn.Core",
        "VRCLearn.Infrastructure",
        "VRCLearnLauncher",
        "Wpf.Ui",
        "Wpf.Ui.Abstractions",
        "CommunityToolkit.Mvvm",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Xaml"
    )

    $entryName = [System.IO.Path]::GetFileName($manifest.entry.assembly)
    $entryDeps = [System.IO.Path]::GetFileNameWithoutExtension($entryName) + ".deps.json"

    Get-ChildItem $publishDir -File | ForEach-Object {
        $name = $_.Name
        $base = [System.IO.Path]::GetFileNameWithoutExtension($name)

        if ($hostAssemblies -contains $base) {
            return
        }

        if ($base.StartsWith("Microsoft.Extensions.", [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }

        if ($name -like "*.pdb" -or $name -like "*.xml") {
            return
        }

        if ($name -like "*.json") {
            if ($name -eq $entryDeps) {
                Copy-Item $_.FullName -Destination $packageDir
            }

            return
        }

        Copy-Item $_.FullName -Destination $packageDir
    }

    Copy-Item $manifestPath -Destination $packageDir

    $localization = Join-Path $projectDir "localization"

    if (Test-Path $localization) {
        Copy-Item $localization -Destination $packageDir -Recurse
    }

    $entryPath = Join-Path $packageDir $entryName

    if (-not (Test-Path $entryPath)) {
        throw "entry assembly missing from package: $entryName"
    }

    $output = Join-Path $root $OutputDirectory
    New-Item -ItemType Directory -Path $output -Force | Out-Null

    Get-ChildItem $output -Filter "$id-*.vclplugin*" -File | Remove-Item -Force

    $zipName = "$id-$version.vclplugin"
    $zipPath = Join-Path $output $zipName

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $packageDir,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Write-Output "package: $zipPath"
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}

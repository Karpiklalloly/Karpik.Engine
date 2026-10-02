param(
    [string] $InstallationRoot = "",

    [string] $SdkVersion = "",

    [string] $EngineVersion = "0.6.0-dev",

    [string] $Output = (Join-Path $PSScriptRoot "..\artifacts\distribution")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
    Splits a built engine installation into distributable bundles.
.DESCRIPTION
    Takes an installation produced by Update-KarpikSdk.ps1 and builds two
    bundles: SDK (engine payload without the editor) and Launcher+Editor.
    Each bundle carries the self-contained setup executable. Nothing is
    published outward; copy the bundles to other machines and run setup.
    Install order on the target machine: 1. SDK bundle, 2. Launcher+Editor.
.EXAMPLE
    ./_scripts/New-KarpikDistribution.ps1 -SdkVersion "0.6.0" -EngineVersion "0.6.0"
.EXAMPLE
    ./_scripts/New-KarpikDistribution.ps1 -InstallationRoot "$env:LOCALAPPDATA\Karpik\Engines\0.6.0-dev-xyz"
#>

if ([string]::IsNullOrWhiteSpace($InstallationRoot)) {
    if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
        throw "Provide either -InstallationRoot or -SdkVersion (with optional -EngineVersion)."
    }
    Write-Host "Building engine installation (SDK $SdkVersion, engine $EngineVersion)..."
    $built = & (Join-Path $PSScriptRoot "Update-KarpikSdk.ps1") -SdkVersion $SdkVersion -EngineVersion $EngineVersion |
        Where-Object { $_ -is [string] -and (Test-Path -LiteralPath (Join-Path $_ "engine-installation.json") -PathType Leaf) } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($built)) {
        throw "Update-KarpikSdk.ps1 did not report an installation directory."
    }
    $InstallationRoot = $built
}

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$InstallationRoot = [IO.Path]::GetFullPath($InstallationRoot)
$InstallName = Split-Path $InstallationRoot -Leaf

$manifestPath = Join-Path $InstallationRoot "engine-installation.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Not an engine installation (no engine-installation.json): $InstallationRoot"
}
if (-not (Test-Path -LiteralPath (Join-Path $InstallationRoot ".complete") -PathType Leaf)) {
    throw "Installation is not complete (no .complete marker): $InstallationRoot"
}
foreach ($dir in @("sdk", "modules", "native", "shared", "runners", "editor")) {
    if (-not (Test-Path -LiteralPath (Join-Path $InstallationRoot $dir) -PathType Container)) {
        throw "Installation is missing '$dir': $InstallationRoot"
    }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.engineVersion -ne $manifest.editorVersion) {
    throw "Manifest engine/editor versions diverge; bundles require a matched build."
}

$DistRoot = [IO.Path]::GetFullPath($Output)
$SdkDist = Join-Path $DistRoot "$InstallName-sdk"
$EditorDist = Join-Path $DistRoot "$InstallName-editor-launcher"
foreach ($dir in @($SdkDist, $EditorDist)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

Write-Host "Publishing setup executable..."
$setupPublish = Join-Path ([IO.Path]::GetTempPath()) ("karpik-setup-pub-" + [Guid]::NewGuid().ToString("N"))
& dotnet publish (Join-Path $RepositoryRoot "Karpik.Engine.Setup\Karpik.Engine.Setup.csproj") `
    -c Release -p:PublishSingleFile=true -p:SelfContained=false -m:1 -nr:false -o $setupPublish
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed." }

Write-Host "Publishing launcher..."
$launcherPublish = Join-Path ([IO.Path]::GetTempPath()) ("karpik-launcher-pub-" + [Guid]::NewGuid().ToString("N"))
& dotnet publish (Join-Path $RepositoryRoot "Karpik.Launcher\Karpik.Launcher.csproj") `
    -c Release -m:1 -nr:false -o $launcherPublish
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }

try {
    $setupExe = Join-Path $setupPublish "setup.exe"
    if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) { throw "Setup publish produced no setup.exe." }

    Write-Host "Packing SDK payload..."
    $sdkStage = Join-Path ([IO.Path]::GetTempPath()) ("karpik-sdk-stage-" + [Guid]::NewGuid().ToString("N"))
    $sdkRoot = Join-Path $sdkStage $InstallName
    New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $InstallationRoot -Force) {
        if ($entry.Name -eq "editor") { continue }
        if ($entry.PSIsContainer) {
            Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $sdkRoot $entry.Name) -Recurse -Force
        }
        else {
            Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $sdkRoot $entry.Name) -Force
        }
    }
    $sdkBundle = $SdkDist
    Copy-Item -Path (Join-Path $setupPublish "*") -Destination $sdkBundle -Recurse -Force -Exclude "*.pdb"
    if (-not (Test-Path -LiteralPath (Join-Path $sdkBundle "setup.exe") -PathType Leaf)) { throw "Setup publish produced no setup.exe." }
    Compress-Archive -Path (Join-Path $sdkStage "*") -DestinationPath (Join-Path $sdkBundle "sdk-payload.zip") -Force
    Remove-Item -LiteralPath $sdkStage -Recurse -Force

    Write-Host "Packing editor payload..."
    $editorStage = Join-Path ([IO.Path]::GetTempPath()) ("karpik-editor-stage-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $editorStage -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $InstallationRoot "editor") -Destination (Join-Path $editorStage "editor") -Recurse -Force
    Set-Content -LiteralPath (Join-Path $editorStage "editor-version.txt") -Value $manifest.engineVersion -NoNewline
    $editorBundle = $EditorDist
    Copy-Item -Path (Join-Path $setupPublish "*") -Destination $editorBundle -Recurse -Force -Exclude "*.pdb"
    Compress-Archive -Path (Join-Path $editorStage "*") -DestinationPath (Join-Path $editorBundle "editor-payload.zip") -Force
    Remove-Item -LiteralPath $editorStage -Recurse -Force

    Write-Host "Packing launcher files..."
    $launcherFiles = Join-Path $editorBundle "launcher-files"
    New-Item -ItemType Directory -Path $launcherFiles -Force | Out-Null
    Copy-Item -Path (Join-Path $launcherPublish "*") -Destination $launcherFiles -Recurse -Force -Exclude "*.pdb"
    if (-not (Test-Path -LiteralPath (Join-Path $launcherFiles "Karpik.Launcher.exe") -PathType Leaf)) {
        throw "Launcher publish produced no Karpik.Launcher.exe."
    }

    Set-Content -LiteralPath (Join-Path $SdkDist "README.txt") -Value @"
KarpikEngine SDK bundle ($InstallName)
SDK $($manifest.msBuildSdkVersion), engine $($manifest.engineVersion).

Contents: setup.exe (installer), sdk-payload.zip (engine payload without the editor).
Requires .NET 10 (setup itself runs on it; game builds need the SDK).

Install (distribute this folder on its own):
  setup.exe sdk --payload sdk-payload.zip

This registers a local NuGet source for the SDK. Games additionally need
the Launcher+Editor bundle ($InstallName-editor-launcher) for the editor.
"@

    Set-Content -LiteralPath (Join-Path $EditorDist "README.txt") -Value @"
KarpikEngine Launcher+Editor bundle ($InstallName)
Editor $($manifest.editorVersion), pairs with SDK $($manifest.msBuildSdkVersion).

Contents: setup.exe (installer), editor-payload.zip (editor payload),
launcher-files/ (launcher application).

Requires .NET 10 and the installed SDK bundle version '$($manifest.engineVersion)'
(install that bundle first). Distribute this folder on its own.

Install:
  setup.exe editor --payload editor-payload.zip
  setup.exe launcher --source launcher-files

Then start the launcher from the Start Menu and create a game from a template.
"@
}
finally {
    Remove-Item -LiteralPath $setupPublish -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $launcherPublish -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Distribution ready: $DistRoot"
Get-ChildItem -LiteralPath $DistRoot -Recurse -File | ForEach-Object {
    Write-Host ("  " + $_.FullName.Replace($DistRoot, ".") + "  (" + [math]::Round($_.Length / 1MB, 1) + " MB)")
}

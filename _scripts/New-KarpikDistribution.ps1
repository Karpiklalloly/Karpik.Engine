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

$DistRoot = [IO.Path]::GetFullPath((Join-Path $Output $InstallName))
foreach ($bundle in @("sdk", "editor-launcher")) {
    $dir = Join-Path $DistRoot $bundle
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

Write-Host "Publishing setup executable..."
$setupPublish = Join-Path ([IO.Path]::GetTempPath()) ("karpik-setup-pub-" + [Guid]::NewGuid().ToString("N"))
& dotnet publish (Join-Path $RepositoryRoot "Karpik.Engine.Setup\Karpik.Engine.Setup.csproj") `
    -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
    -m:1 -nr:false -o $setupPublish
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed." }

Write-Host "Publishing launcher..."
$launcherPublish = Join-Path ([IO.Path]::GetTempPath()) ("karpik-launcher-pub-" + [Guid]::NewGuid().ToString("N"))
& dotnet publish (Join-Path $RepositoryRoot "Karpik.Launcher\Karpik.Launcher.csproj") `
    -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
    -m:1 -nr:false -o $launcherPublish
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
    $sdkBundle = Join-Path $DistRoot "sdk"
    Copy-Item -LiteralPath $setupExe -Destination (Join-Path $sdkBundle "setup.exe") -Force
    Compress-Archive -Path (Join-Path $sdkStage "*") -DestinationPath (Join-Path $sdkBundle "sdk-payload.zip") -Force
    Remove-Item -LiteralPath $sdkStage -Recurse -Force

    Write-Host "Packing editor payload..."
    $editorStage = Join-Path ([IO.Path]::GetTempPath()) ("karpik-editor-stage-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $editorStage -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $InstallationRoot "editor") -Destination (Join-Path $editorStage "editor") -Recurse -Force
    Set-Content -LiteralPath (Join-Path $editorStage "editor-version.txt") -Value $manifest.engineVersion -NoNewline
    $editorBundle = Join-Path $DistRoot "editor-launcher"
    Copy-Item -LiteralPath $setupExe -Destination (Join-Path $editorBundle "setup.exe") -Force
    Compress-Archive -Path (Join-Path $editorStage "*") -DestinationPath (Join-Path $editorBundle "editor-payload.zip") -Force
    Remove-Item -LiteralPath $editorStage -Recurse -Force

    Write-Host "Packing launcher files..."
    $launcherFiles = Join-Path $editorBundle "launcher-files"
    New-Item -ItemType Directory -Path $launcherFiles -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $launcherPublish -File) {
        if ($file.Extension -in ".pdb") { continue }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $launcherFiles $file.Name) -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $launcherFiles "Karpik.Launcher.exe") -PathType Leaf)) {
        throw "Launcher publish produced no Karpik.Launcher.exe."
    }

    Set-Content -LiteralPath (Join-Path $DistRoot "README.txt") -Value @"
KarpikEngine distribution ($InstallName)
SDK $($manifest.msBuildSdkVersion), engine/editor $($manifest.engineVersion).

Install order on the target machine (.NET 10 SDK required for game builds):
  1. sdk\setup.exe sdk --payload sdk\sdk-payload.zip
  2. editor-launcher\setup.exe editor --payload editor-launcher\editor-payload.zip
  3. editor-launcher\setup.exe launcher --source editor-launcher\launcher-files
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

param(
    [Parameter(Mandatory)]
    [string] $SdkVersion,

    [string] $Output = (Join-Path $PSScriptRoot "..\artifacts\sdk-packages")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
    Builds only the distributable SDK NuGet packages for a given version.
.DESCRIPTION
    Packs Karpik.Engine.Sdk and Karpik.Engine.Templates with -p:PackageVersion
    into the output directory. Nothing is installed or registered: no engine
    payload, no NuGet source, no environment changes. Publishing the resulting
    .nupkg files (dotnet nuget push or a file feed) is a separate manual step.
.EXAMPLE
    ./_scripts/New-KarpikSdkPackages.ps1 -SdkVersion "0.6.0"
#>

if ([string]::IsNullOrWhiteSpace($SdkVersion) -or
    $SdkVersion -in ".", ".." -or
    $SdkVersion.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
    $SdkVersion.Contains([IO.Path]::DirectorySeparatorChar) -or
    $SdkVersion.Contains([IO.Path]::AltDirectorySeparatorChar)) {
    throw "SdkVersion must be a safe non-empty version value."
}

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$PayloadProjects = @(
    "Karpik.Engine.Sdk.Tasks\Karpik.Engine.Sdk.Tasks.csproj",
    "Karpik.Engine.Core.Generator\Karpik.Engine.Core.Codegen\Karpik.Engine.Core.Codegen.csproj",
    "Karpik.Content.Tool\Karpik.Content.Tool.csproj",
    "Karpik.Content.Codegen\Karpik.Content.Codegen.csproj",
    "Karpik.Content.Runtime\Karpik.Content.Runtime.csproj",
    "Network.Codegen\Network.Codegen\Network.Codegen.csproj"
)
$PackProjects = @(
    "Karpik.Engine.Sdk\Karpik.Engine.Sdk.csproj",
    "Karpik.Engine.Templates\Karpik.Engine.Templates.csproj"
)

foreach ($project in ($PayloadProjects + $PackProjects)) {
    $path = Join-Path $RepositoryRoot $project
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Project was not found: $path"
    }
}

$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null

Write-Host "Restoring SDK payload projects..."
foreach ($project in ($PayloadProjects + $PackProjects)) {
    & dotnet restore (Join-Path $RepositoryRoot $project) -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed for $project."
    }
}

Write-Host "Packing SDK packages $SdkVersion -> $Output"
foreach ($project in $PackProjects) {
    & dotnet pack (Join-Path $RepositoryRoot $project) -c Release --no-restore `
        -m:1 -nr:false "-p:PackageVersion=$SdkVersion" -o $Output
    if ($LASTEXITCODE -ne 0) {
        throw "Pack failed for $project."
    }
}

$packages = Get-ChildItem -LiteralPath $Output -Filter "*.nupkg" | Sort-Object Name
if ($packages.Count -eq 0) {
    throw "No packages were produced in $Output."
}

Write-Host ""
Write-Host "SDK packages ready:"
foreach ($package in $packages) {
    Write-Host "  $($package.FullName)"
}
Write-Host ""
Write-Host "Publish them yourself, for example:"
Write-Host "  dotnet nuget push `"$Output\Karpik.Engine.Sdk.$SdkVersion.nupkg`" --source <feed> --api-key <key>"
Write-Host "Note: games also need an engine installation (module payload);"
Write-Host "these packages alone provide targets, tasks, generators and templates."

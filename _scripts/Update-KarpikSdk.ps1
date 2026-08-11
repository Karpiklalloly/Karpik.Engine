Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Change this value when the MSBuild SDK version changes.
$SdkVersion = "0.6.0-local"

# Development engine versions are content-hash qualified by the packager.
$EngineVersion = "0.6.0-dev"

$RepositoryRoot = $PSScriptRoot
$PackagerProject = Join-Path $RepositoryRoot "Karpik.Engine.Packager\Karpik.Engine.Packager.csproj"
$EnvironmentModule = Join-Path $RepositoryRoot "Karpik.Sdk.Environment.psm1"
$KarpikHome = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Karpik"
$EnginesRoot = Join-Path $KarpikHome "Engines"
$ArchiveRoot = Join-Path $KarpikHome "ArchivedEngines"
$NuGetSourceName = "KarpikEngine-$SdkVersion"
$IsWindowsPlatform = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

function Assert-SafeVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -in ".", ".." -or
        $Value.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $Value.Contains([IO.Path]::DirectorySeparatorChar) -or
        $Value.Contains([IO.Path]::AltDirectorySeparatorChar)) {
        throw "$Name must be a safe non-empty version value."
    }
}

function Test-PathInside {
    param(
        [Parameter(Mandatory)]
        [string] $Candidate,

        [Parameter(Mandatory)]
        [string] $Root
    )

    $fullCandidate = [IO.Path]::GetFullPath($Candidate)
    $fullRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, "."))
    $comparison = if ($IsWindowsPlatform) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }

    $rootPrefix = if ($fullRoot.EndsWith([IO.Path]::DirectorySeparatorChar) -or
        $fullRoot.EndsWith([IO.Path]::AltDirectorySeparatorChar)) {
        $fullRoot
    }
    else {
        $fullRoot + [IO.Path]::DirectorySeparatorChar
    }

    return $fullCandidate.Equals($fullRoot, $comparison) -or
        $fullCandidate.StartsWith($rootPrefix, $comparison)
}

function Read-InstallationManifest {
    param(
        [Parameter(Mandatory)]
        [string] $InstallationRoot
    )

    $manifestPath = Join-Path $InstallationRoot "engine-installation.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Skipping installation with an unreadable manifest: $InstallationRoot"
        return $null
    }
}

Assert-SafeVersion -Value $SdkVersion -Name "SdkVersion"
Assert-SafeVersion -Value $EngineVersion -Name "EngineVersion"

if (-not (Test-Path -LiteralPath $PackagerProject -PathType Leaf)) {
    throw "Karpik.Engine.Packager was not found: $PackagerProject"
}

if (-not (Test-Path -LiteralPath $EnvironmentModule -PathType Leaf)) {
    throw "Karpik SDK environment module was not found: $EnvironmentModule"
}

Import-Module $EnvironmentModule -Force

New-Item -ItemType Directory -Path $EnginesRoot -Force | Out-Null

Write-Host "Building KarpikEngine SDK $SdkVersion and engine $EngineVersion..."
$packagerOutput = @(
    & dotnet run --project $PackagerProject -- `
        --source $RepositoryRoot `
        --output $KarpikHome `
        --engine-version $EngineVersion `
        --sdk-version $SdkVersion 2>&1 |
        Tee-Object -Variable displayedPackagerOutput
)
$packagerExitCode = $LASTEXITCODE
if ($packagerExitCode -ne 0) {
    throw "Karpik.Engine.Packager failed with exit code $packagerExitCode."
}

$installationRoot = $null
foreach ($line in $packagerOutput) {
    $text = $line.ToString()
    if ($text -match "^(?:Published|Reused) engine payload:\s*(.+)$") {
        $installationRoot = [IO.Path]::GetFullPath($Matches[1].Trim())
    }
}

if ([string]::IsNullOrWhiteSpace($installationRoot) -or
    -not (Test-PathInside -Candidate $installationRoot -Root $EnginesRoot) -or
    -not (Test-Path -LiteralPath $installationRoot -PathType Container)) {
    throw "Packager did not report a valid installation below $EnginesRoot."
}

$newManifest = Read-InstallationManifest -InstallationRoot $installationRoot
if ($null -eq $newManifest -or
    $newManifest.msBuildSdkVersion -ne $SdkVersion -or
    $newManifest.engineVersion -ne $EngineVersion -or
    -not (Test-Path -LiteralPath (Join-Path $installationRoot ".complete") -PathType Leaf)) {
    throw "The newly published installation did not pass the script contract checks: $installationRoot"
}

Write-Host "Updating KarpikEngineRoot -> $installationRoot"
Set-KarpikEngineRootEnvironment -InstallationRoot $installationRoot

$matchingPreviousInstallations = @(
    Get-ChildItem -LiteralPath $EnginesRoot -Directory |
        Where-Object {
            -not [IO.Path]::GetFullPath($_.FullName).Equals(
                [IO.Path]::GetFullPath($installationRoot),
                $(if ($IsWindowsPlatform) {
                    [StringComparison]::OrdinalIgnoreCase
                }
                else {
                    [StringComparison]::Ordinal
                }))
        } |
        Where-Object {
            $manifest = Read-InstallationManifest -InstallationRoot $_.FullName
            $null -ne $manifest -and $manifest.msBuildSdkVersion -eq $SdkVersion
        }
)

if ($matchingPreviousInstallations.Count -gt 0) {
    New-Item -ItemType Directory -Path $ArchiveRoot -Force | Out-Null
    $archiveStamp = Get-Date -Format "yyyyMMdd-HHmmss"
    foreach ($previous in $matchingPreviousInstallations) {
        if (-not (Test-PathInside -Candidate $previous.FullName -Root $EnginesRoot)) {
            throw "Refusing to archive an installation outside $EnginesRoot`: $($previous.FullName)"
        }

        $archiveName = "$archiveStamp-$($previous.Name)"
        $archivePath = Join-Path $ArchiveRoot $archiveName
        if (Test-Path -LiteralPath $archivePath) {
            $archivePath = Join-Path $ArchiveRoot "$archiveName-$([Guid]::NewGuid().ToString('N'))"
        }

        Write-Host "Archiving previous SDK installation: $($previous.FullName)"
        Move-Item -LiteralPath $previous.FullName -Destination $archivePath
    }
}

$sdkFeed = Join-Path $installationRoot "sdk"
Write-Host "Updating NuGet source $NuGetSourceName -> $sdkFeed"
& dotnet nuget remove source $NuGetSourceName 2>&1 | Out-Null
& dotnet nuget add source $sdkFeed --name $NuGetSourceName
if ($LASTEXITCODE -ne 0) {
    throw "Failed to register NuGet source '$NuGetSourceName'."
}

$nugetPackagesRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $HOME ".nuget\packages"
}
else {
    [IO.Path]::GetFullPath($env:NUGET_PACKAGES)
}
$sdkCachePath = Join-Path (Join-Path $nugetPackagesRoot "karpik.engine.sdk") $SdkVersion.ToLowerInvariant()
if (Test-Path -LiteralPath $sdkCachePath) {
    if (-not (Test-PathInside -Candidate $sdkCachePath -Root $nugetPackagesRoot)) {
        throw "Refusing to clear an SDK cache outside the NuGet package root: $sdkCachePath"
    }

    Write-Host "Removing cached Karpik.Engine.Sdk $SdkVersion..."
    Remove-Item -LiteralPath $sdkCachePath -Recurse -Force
}

Write-Host ""
Write-Host "KarpikEngine SDK update completed."
Write-Host "SDK version: $SdkVersion"
Write-Host "Installation: $installationRoot"

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter()]
    [ValidateRange(0, 1000)]
    [int] $KeepCount = 5,

    [Parameter()]
    [string] $KarpikHome = (Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Karpik"),

    [Parameter()]
    [string] $CurrentInstallation = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$IsWindowsPlatform = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

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

function Test-SamePath {
    param(
        [Parameter(Mandatory)]
        [string] $Left,

        [Parameter(Mandatory)]
        [string] $Right
    )

    $comparison = if ($IsWindowsPlatform) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }

    return [IO.Path]::GetFullPath($Left).Equals([IO.Path]::GetFullPath($Right), $comparison)
}

function Get-DirectorySizeBytes {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $measured = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum
    if ($null -eq $measured -or $null -eq $measured.Sum) {
        return [long]0
    }
    return [long]$measured.Sum
}

function Remove-InstallationDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $StoreRoot,

        [Parameter(Mandatory)]
        [string] $Reason
    )

    if (-not (Test-PathInside -Candidate $Path -Root $StoreRoot)) {
        throw "Refusing to delete an installation outside $StoreRoot`: $Path"
    }

    $sizeBytes = Get-DirectorySizeBytes -Path $Path
    if ($PSCmdlet.ShouldProcess($Path, "Remove old SDK installation ($Reason)")) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    return $sizeBytes
}

if ([string]::IsNullOrWhiteSpace($KarpikHome)) {
    throw "KarpikHome must be a non-empty path."
}
$fullKarpikHome = [IO.Path]::GetFullPath($KarpikHome)
$enginesRoot = Join-Path $fullKarpikHome "Engines"
$archiveRoot = Join-Path $fullKarpikHome "ArchivedEngines"

$protectedRoots = @()
foreach ($candidate in @($CurrentInstallation, $env:KarpikEngineRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $protectedRoots += [IO.Path]::GetFullPath($candidate)
    }
}

function Test-IsProtected {
    param([Parameter(Mandatory)][string] $Path)

    foreach ($protected in $protectedRoots) {
        if (Test-SamePath -Left $Path -Right $protected) {
            return $true
        }
    }
    return $false
}

$totalFreedBytes = [long]0

# Engines: incomplete payloads (no .complete marker) never participate in
# resolution, so they are always garbage. The rest is trimmed by age.
if (Test-Path -LiteralPath $enginesRoot -PathType Container) {
    $installations = @(Get-ChildItem -LiteralPath $enginesRoot -Directory -ErrorAction SilentlyContinue)
    $removedIncomplete = 0
    $remaining = @()
    foreach ($installation in $installations) {
        $isComplete = Test-Path -LiteralPath (Join-Path $installation.FullName ".complete") -PathType Leaf
        if (-not $isComplete -and -not (Test-IsProtected -Path $installation.FullName)) {
            $totalFreedBytes += Remove-InstallationDirectory `
                -Path $installation.FullName -StoreRoot $enginesRoot -Reason "incomplete payload"
            $removedIncomplete++
        }
        else {
            $remaining += $installation
        }
    }

    $sorted = @($remaining | Sort-Object -Property LastWriteTimeUtc -Descending)
    $keep = @()
    $remove = @()
    for ($index = 0; $index -lt $sorted.Count; $index++) {
        if ($keep.Count -ge $KeepCount -and -not (Test-IsProtected -Path $sorted[$index].FullName)) {
            $remove += $sorted[$index]
        }
        else {
            $keep += $sorted[$index]
        }
    }
    foreach ($stale in $remove) {
        $totalFreedBytes += Remove-InstallationDirectory `
            -Path $stale.FullName -StoreRoot $enginesRoot -Reason "older than newest $KeepCount"
    }

    Write-Host "Engines: kept $($keep.Count), removed $($removedIncomplete + $remove.Count) ($removedIncomplete incomplete)."
}
else {
    Write-Host "Engines: store not found, nothing to clean: $enginesRoot"
}

# ArchivedEngines: nothing reads this store (plain backups), directory names
# carry the archive timestamp prefix, so newest-first by name is exact.
if (Test-Path -LiteralPath $archiveRoot -PathType Container) {
    $archived = @(
        Get-ChildItem -LiteralPath $archiveRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object -Property Name -Descending
    )
    $keptArchive = 0
    $removedArchive = 0
    foreach ($entry in $archived) {
        if ($keptArchive -ge $KeepCount -and -not (Test-IsProtected -Path $entry.FullName)) {
            $totalFreedBytes += Remove-InstallationDirectory `
                -Path $entry.FullName -StoreRoot $archiveRoot -Reason "older than newest $KeepCount"
            $removedArchive++
        }
        else {
            $keptArchive++
        }
    }

    Write-Host "ArchivedEngines: kept $keptArchive, removed $removedArchive."
}
else {
    Write-Host "ArchivedEngines: store not found, nothing to clean: $archiveRoot"
}

# NuGet global-packages: every publish mints a timestamped SDK version and the
# first restore extracts ~29 MB here forever. Old versions self-heal on the
# next restore (re-extracted from the engine-local sdk feed), so trim by age.
$nugetPackagesRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $HOME ".nuget\packages"
}
else {
    [IO.Path]::GetFullPath($env:NUGET_PACKAGES)
}
$sdkPackageRoot = Join-Path $nugetPackagesRoot "karpik.engine.sdk"
if (Test-Path -LiteralPath $sdkPackageRoot -PathType Container) {
    $sdkVersions = @(
        Get-ChildItem -LiteralPath $sdkPackageRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object -Property LastWriteTimeUtc -Descending
    )
    $keptSdk = 0
    $removedSdk = 0
    foreach ($version in $sdkVersions) {
        if ($keptSdk -ge $KeepCount) {
            $totalFreedBytes += Remove-InstallationDirectory `
                -Path $version.FullName -StoreRoot $sdkPackageRoot -Reason "older than newest $KeepCount"
            $removedSdk++
        }
        else {
            $keptSdk++
        }
    }

    Write-Host "NuGet karpik.engine.sdk: kept $keptSdk, removed $removedSdk."
}
else {
    Write-Host "NuGet karpik.engine.sdk: package not found, nothing to clean: $sdkPackageRoot"
}

# NuGet sources: every publish registers a versioned KarpikEngine-* source.
# Drop entries whose local feed directory is gone (dead pointers only, http
# sources and everything else are never touched).
$nugetConfigPath = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "NuGet\NuGet.Config"
if (Test-Path -LiteralPath $nugetConfigPath -PathType Leaf) {
    try {
        [xml]$nugetConfig = Get-Content -LiteralPath $nugetConfigPath -Raw
        $deadSources = @()
        foreach ($add in $nugetConfig.configuration.packageSources.add) {
            $name = [string]$add.key
            $value = [string]$add.value
            if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($value)) {
                continue
            }
            if ($name -notlike "KarpikEngine-*") {
                continue
            }
            if ($value -match '^[a-zA-Z][a-zA-Z0-9+.-]*://') {
                continue
            }
            if (-not (Test-Path -LiteralPath $value -PathType Container)) {
                $deadSources += $name
            }
        }
        $removedSources = 0
        foreach ($dead in $deadSources) {
            if ($PSCmdlet.ShouldProcess($dead, "Remove dead NuGet source")) {
                & dotnet nuget remove source $dead 2>&1 | Out-Null
            }
            $removedSources++
        }

        Write-Host "NuGet sources: removed $removedSources dead KarpikEngine-* entries."
    }
    catch {
        Write-Warning "NuGet source sweep failed (installations are unaffected): $_"
    }
}
else {
    Write-Host "NuGet sources: user config not found, nothing to clean."
}

Write-Host ("Retention cleanup finished. Freed {0:N1} MB." -f ($totalFreedBytes / 1MB))

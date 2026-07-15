param(
    [Parameter(Mandatory = $true)]
    [string] $PackageFeed,

    [string] $PackageVersion = "0.6.0-local"
)

$ErrorActionPreference = "Stop"
$packageFeedPath = (Resolve-Path $PackageFeed).Path
$templatePath = Join-Path $PSScriptRoot "..\Karpik.Engine.Sdk\Templates\Directory.Solution.targets"
$root = Join-Path ([IO.Path]::GetTempPath()) ("KarpikSdkImportedReferenceSmoke_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path (Join-Path $root "Client") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root "Shared") -Force | Out-Null
    Copy-Item -LiteralPath $templatePath -Destination (Join-Path $root "Directory.Solution.targets")

    Set-Content -LiteralPath (Join-Path $root "global.json") -Encoding utf8 -Value @"
{
  "sdk": { "version": "10.0.100", "rollForward": "latestPatch" },
  "msbuild-sdks": { "Karpik.Engine.Sdk": "$PackageVersion" }
}
"@

    Set-Content -LiteralPath (Join-Path $root "NuGet.Config") -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="packages" /></config>
  <packageSources>
    <clear />
    <add key="Karpik local" value="$packageFeedPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    Set-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Encoding utf8 -Value @"
<Project>
  <ItemGroup Condition="'`$(MSBuildProjectName)' == 'Client'">
    <ProjectReference Include="`$(MSBuildThisFileDirectory)Shared\Shared.csproj" />
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "Client\Client.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Client</KarpikSide>
  </PropertyGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "Client\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_IMPORTED_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN
public sealed class CompilerMarker;
"@

    Set-Content -LiteralPath (Join-Path $root "Shared\Shared.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "Shared\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_IMPORTED_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN
public sealed class SharedCompilerMarker;
"@

    $solutions = @(
        @{
            Name = "imported same-side project present in solution"
            Projects = @("Client/Client.csproj", "Shared/Shared.csproj")
        },
        @{
            Name = "imported project omitted from solution"
            Projects = @("Client/Client.csproj")
        }
    )

    foreach ($scenario in $solutions) {
        $projectLines = $scenario.Projects | ForEach-Object { "  <Project Path=`"$_`" />" }
        Set-Content -LiteralPath (Join-Path $root "Game.slnx") -Encoding utf8 -Value @"
<Solution>
$($projectLines -join "`n")
</Solution>
"@

        Push-Location $root
        try {
            $buildOutput = & dotnet build Game.slnx -m:1 -nr:false -p:RestoreDisableParallel=true -p:NuGetAudit=false --tl:off -v:minimal 2>&1
            $buildExitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        $buildText = $buildOutput -join "`n"
        $buildOutput | Write-Output

        if ($buildExitCode -eq 0) {
            throw "Invalid imported reference unexpectedly built successfully: $($scenario.Name)."
        }
        if ($buildText.IndexOf("KARPIK004", [StringComparison]::Ordinal) -lt 0) {
            throw "Imported reference did not emit KARPIK004: $($scenario.Name)."
        }
        if ($buildText.IndexOf("KARPIK005", [StringComparison]::Ordinal) -ge 0) {
            throw "Otherwise valid same-side import was treated as a side violation instead of a structural mismatch: $($scenario.Name)."
        }
        if ($buildText.IndexOf("error CS", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $buildText.IndexOf("KARPIK_IMPORTED_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0) {
            throw "Compilation started before imported-reference validation completed: $($scenario.Name)."
        }

        Write-Output "Imported-reference smoke passed for '$($scenario.Name)': KARPIK004 occurred before compilation."
    }

    Remove-Item -LiteralPath (Join-Path $root "Directory.Build.props")
    Set-Content -LiteralPath (Join-Path $root "Client\Client.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Client</KarpikSide>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Shared\Shared.csproj" Condition="'`$(IncludeShared)' == 'true'" />
  </ItemGroup>
</Project>
"@

    Push-Location $root
    try {
        $buildOutput = & dotnet build Client\Client.csproj -m:1 -nr:false -p:RestoreDisableParallel=true -p:NuGetAudit=false --tl:off -v:minimal 2>&1
        $buildExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $buildText = $buildOutput -join "`n"
    $buildOutput | Write-Output

    if ($buildExitCode -eq 0 -or $buildText.IndexOf("KARPIK004", [StringComparison]::Ordinal) -lt 0) {
        throw "Conditionally omitted static reference did not fail with KARPIK004."
    }
    if ($buildText.IndexOf("error CS", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $buildText.IndexOf("KARPIK_IMPORTED_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0) {
        throw "Compilation started before conditional-reference mismatch validation completed."
    }

    Write-Output "Conditional-reference smoke passed: raw-only edge emitted KARPIK004 before compilation."
}
finally {
    if (Test-Path -LiteralPath $root) {
        $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (!$resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove smoke directory outside the temporary root: $resolvedRoot"
        }
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}

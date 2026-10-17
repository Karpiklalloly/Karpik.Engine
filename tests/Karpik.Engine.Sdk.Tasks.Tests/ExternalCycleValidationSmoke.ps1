param(
    [Parameter(Mandatory = $true)]
    [string] $PackageFeed,

    [string] $PackageVersion = "0.6.0-local"
)

$ErrorActionPreference = "Stop"
$packageFeedPath = (Resolve-Path $PackageFeed).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("KarpikSdkCycleSmoke_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path (Join-Path $root "A") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root "B") -Force | Out-Null

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

    Set-Content -LiteralPath (Join-Path $root "A\A.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\B\B.csproj" />
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "B\B.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\A\A.csproj" />
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "A\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_CYCLE_COMPILER_MARKER_MUST_NOT_RUN
public sealed class ACompilerMarker;
"@
    Set-Content -LiteralPath (Join-Path $root "B\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_CYCLE_COMPILER_MARKER_MUST_NOT_RUN
public sealed class BCompilerMarker;
"@

    Push-Location $root
    try {
        $buildOutput = & dotnet build A\A.csproj -m:1 -nr:false -p:RestoreDisableParallel=true -p:NuGetAudit=false --tl:off -v:minimal 2>&1
        $buildExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $buildText = $buildOutput -join "`n"
    $buildOutput | Write-Output

    if ($buildExitCode -eq 0) {
        throw "Literal ProjectReference cycle unexpectedly built successfully."
    }
    if ($buildText.IndexOf("KARPIK006", [StringComparison]::Ordinal) -lt 0) {
        throw "Literal ProjectReference cycle did not emit KARPIK006."
    }
    if ($buildText.IndexOf("MSB4006", [StringComparison]::Ordinal) -ge 0) {
        throw "NuGet restore graph traversal ran before Karpik cycle validation."
    }
    if ($buildText.IndexOf("error CS", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $buildText.IndexOf("KARPIK_CYCLE_COMPILER_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0) {
        throw "Compilation started before literal-cycle validation completed."
    }

    Write-Output "Literal-cycle smoke passed: KARPIK006 occurred before compilation."
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

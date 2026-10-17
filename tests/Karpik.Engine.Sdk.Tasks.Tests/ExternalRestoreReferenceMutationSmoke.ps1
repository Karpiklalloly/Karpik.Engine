param(
    [Parameter(Mandatory = $true)]
    [string] $PackageFeed,

    [string] $PackageVersion = "0.6.0-local"
)

$ErrorActionPreference = "Stop"
$packageFeedPath = (Resolve-Path $PackageFeed).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("KarpikSdkRestoreReferenceSmoke_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path (Join-Path $root "Client") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root "Shared") -Force | Out-Null

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

    Set-Content -LiteralPath (Join-Path $root "Directory.Build.targets") -Encoding utf8 -Value @"
<Project>
  <Target Name="AddRestoreWalkProjectReference" BeforeTargets="_GenerateRestoreProjectPathWalk"
          Condition="'`$(MSBuildProjectName)' == 'Client'">
    <ItemGroup>
      <ProjectReference Include="`$(MSBuildThisFileDirectory)Shared\Shared.csproj" />
    </ItemGroup>
  </Target>
  <Target Name="RestoreWalkMarker" AfterTargets="_GenerateRestoreProjectPathWalk"
          Condition="'`$(MSBuildProjectName)' == 'Client'">
    <Error Text="KARPIK_RESTORE_REFERENCE_MARKER_MUST_NOT_RUN" />
  </Target>
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

    Set-Content -LiteralPath (Join-Path $root "Shared\Shared.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
</Project>
"@

    foreach ($markerDirectory in @("Client", "Shared")) {
        Set-Content -LiteralPath (Join-Path $root "$markerDirectory\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_RESTORE_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN
public sealed class CompilerMarker;
"@
    }

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
        throw "Restore-walk ProjectReference mutation did not fail with KARPIK004."
    }
    if ($buildText.IndexOf("MSB4006", [StringComparison]::Ordinal) -ge 0 -or
        $buildText.IndexOf("KARPIK_RESTORE_REFERENCE_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0 -or
        $buildText.IndexOf("KARPIK_RESTORE_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0 -or
        $buildText.IndexOf("error CS", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "NuGet restore walk, reference resolution, or compilation ran before restore-mutation validation."
    }

    Write-Output "Restore-reference smoke passed: KARPIK004 occurred before NuGet restore graph traversal."
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

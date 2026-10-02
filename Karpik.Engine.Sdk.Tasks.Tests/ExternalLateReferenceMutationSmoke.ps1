param(
    [Parameter(Mandatory = $true)]
    [string] $PackageFeed,

    [string] $PackageVersion = "0.6.0-local"
)

$ErrorActionPreference = "Stop"
$packageFeedPath = (Resolve-Path $PackageFeed).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("KarpikSdkLateReferenceSmoke_" + [Guid]::NewGuid().ToString("N"))

try {
    foreach ($directory in @("ClientInline", "Server", "ClientImported", "Shared")) {
        New-Item -ItemType Directory -Path (Join-Path $root $directory) -Force | Out-Null
    }

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

    Set-Content -LiteralPath (Join-Path $root "ClientInline\ClientInline.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Client</KarpikSide>
  </PropertyGroup>
  <Target Name="AddInlineLateProjectReference" BeforeTargets="AssignProjectConfiguration">
    <ItemGroup>
      <ProjectReference Include="`$(MSBuildThisFileDirectory)..\Server\Server.csproj" />
    </ItemGroup>
  </Target>
  <Target Name="InlineReferenceResolutionMarker" AfterTargets="AssignProjectConfiguration">
    <Error Text="KARPIK_LATE_REFERENCE_RESOLUTION_MARKER_MUST_NOT_RUN" />
  </Target>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "ClientImported\LateMutation.targets") -Encoding utf8 -Value @"
<Project>
  <Target Name="AddImportedLateProjectReference" BeforeTargets="AssignProjectConfiguration">
    <ItemGroup>
      <ProjectReference Include="`$(MSBuildThisFileDirectory)..\Shared\Shared.csproj" />
    </ItemGroup>
  </Target>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "ClientImported\ClientImported.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Client</KarpikSide>
  </PropertyGroup>
  <Import Project="LateMutation.targets" />
  <Target Name="ImportedReferenceResolutionMarker" AfterTargets="AssignProjectConfiguration">
    <Error Text="KARPIK_LATE_REFERENCE_RESOLUTION_MARKER_MUST_NOT_RUN" />
  </Target>
</Project>
"@

    foreach ($targetProject in @(
        @{ Path = "Server\Server.csproj"; Side = "Server" },
        @{ Path = "Shared\Shared.csproj"; Side = "Shared" }
    )) {
        Set-Content -LiteralPath (Join-Path $root $targetProject.Path) -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>$($targetProject.Side)</KarpikSide>
  </PropertyGroup>
</Project>
"@
    }

    foreach ($markerDirectory in @("ClientInline", "Server", "ClientImported", "Shared")) {
        Set-Content -LiteralPath (Join-Path $root "$markerDirectory\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_LATE_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN
public sealed class CompilerMarker;
"@
    }

    $scenarios = @(
        @{ Name = "inline Client-to-Server mutation"; Project = "ClientInline\ClientInline.csproj" },
        @{ Name = "imported same-side mutation"; Project = "ClientImported\ClientImported.csproj" }
    )

    foreach ($scenario in $scenarios) {
        Push-Location $root
        try {
            & dotnet restore $scenario.Project -m:1 -nr:false -p:RestoreDisableParallel=true -p:NuGetAudit=false --tl:off -v:minimal
            $restoreExitCode = $LASTEXITCODE
            if ($restoreExitCode -ne 0) {
                throw "Canonical restore failed before late-mutation build: $($scenario.Name)."
            }

            $buildOutput = & dotnet build $scenario.Project --no-restore -m:1 -nr:false -p:NuGetAudit=false --tl:off -v:minimal 2>&1
            $buildExitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        $buildText = $buildOutput -join "`n"
        $buildOutput | Write-Output

        if ($buildExitCode -eq 0 -or $buildText.IndexOf("KARPIK004", [StringComparison]::Ordinal) -lt 0) {
            throw "Late ProjectReference mutation did not fail with KARPIK004: $($scenario.Name)."
        }
        if ($buildText.IndexOf("KARPIK_LATE_REFERENCE_RESOLUTION_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0 -or
            $buildText.IndexOf("KARPIK_LATE_REFERENCE_COMPILER_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0 -or
            $buildText.IndexOf("error CS", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Reference resolution or compilation ran before late ProjectReference validation: $($scenario.Name)."
        }

        Write-Output "Late-reference smoke passed for '$($scenario.Name)': KARPIK004 occurred before reference resolution."
    }
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

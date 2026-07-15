param(
    [Parameter(Mandatory = $true)]
    [string] $PackageFeed,

    [string] $PackageVersion = "0.6.0-local"
)

$ErrorActionPreference = "Stop"
$packageFeedPath = (Resolve-Path $PackageFeed).Path
$templatePath = Join-Path $PSScriptRoot "..\Karpik.Engine.Sdk\Templates\Directory.Solution.targets"
$root = Join-Path ([IO.Path]::GetTempPath()) ("KarpikSdkSolutionSmoke_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path (Join-Path $root "Karpik") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root "Foreign") -Force | Out-Null
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
  <config>
    <add key="globalPackagesFolder" value="packages" />
  </config>
  <packageSources>
    <clear />
    <add key="Karpik local" value="$packageFeedPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    Set-Content -LiteralPath (Join-Path $root "Karpik\Karpik.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "Foreign\Foreign.csproj") -Encoding utf8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $root "Foreign\CompilerMarker.cs") -Encoding utf8 -Value @"
#error KARPIK_FOREIGN_COMPILER_MARKER_MUST_NOT_RUN
public sealed class CompilerMarker;
"@

    $projectOrders = @(
        @("Foreign/Foreign.csproj", "Karpik/Karpik.csproj"),
        @("Karpik/Karpik.csproj", "Foreign/Foreign.csproj")
    )

    foreach ($projectOrder in $projectOrders) {
        $firstProject = $projectOrder[0]
        $secondProject = $projectOrder[1]
        Set-Content -LiteralPath (Join-Path $root "Game.slnx") -Encoding utf8 -Value @"
<Solution>
  <Project Path="$firstProject" />
  <Project Path="$secondProject" />
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
            throw "Invalid solution unexpectedly built successfully for order '$firstProject, $secondProject'."
        }
        if ($buildText.IndexOf("KARPIK001", [StringComparison]::Ordinal) -lt 0) {
            throw "Invalid solution did not emit KARPIK001 for order '$firstProject, $secondProject'."
        }
        if ($buildText.IndexOf("error CS", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $buildText.IndexOf("KARPIK_FOREIGN_COMPILER_MARKER_MUST_NOT_RUN", [StringComparison]::Ordinal) -ge 0) {
            throw "Foreign compilation started before solution validation completed for order '$firstProject, $secondProject'."
        }

        Write-Output "Solution-scope validation passed for order '$firstProject, $secondProject': KARPIK001 occurred before compilation."
    }

    Set-Content -LiteralPath (Join-Path $root "Foreign\Foreign.csproj") -Encoding utf8 -Value @"
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
</Project>
"@
    Remove-Item -LiteralPath (Join-Path $root "Foreign\CompilerMarker.cs")

    Push-Location $root
    try {
        & dotnet build Game.slnx -m:1 -nr:false -p:RestoreDisableParallel=true -p:NuGetAudit=false --tl:off -v:minimal
        $validBuildExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($validBuildExitCode -ne 0) {
        throw "Valid solution smoke failed."
    }

    Write-Output "Valid solution-scope SDK restore/build smoke passed."
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

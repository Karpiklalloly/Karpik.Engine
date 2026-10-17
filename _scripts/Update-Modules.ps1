Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


$ConfiguratorProject = Join-Path $PSScriptRoot "..\src\Configurator\Configurator.csproj"
dotnet run --project $ConfiguratorProject -- --generate
if ($LASTEXITCODE -ne 0) { throw "Module catalog generation failed." }
dotnet run --project $ConfiguratorProject -- --validate
if ($LASTEXITCODE -ne 0) { throw "Module catalog validation failed." }

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


dotnet run --project ..\Configurator\Configurator.csproj -- --generate
dotnet run --project ..\Configurator\Configurator.csproj -- --validate
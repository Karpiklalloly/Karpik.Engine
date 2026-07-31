Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Set-KarpikEngineRootEnvironment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallationRoot
    )

    $fullInstallationRoot = [IO.Path]::GetFullPath($InstallationRoot)
    $env:KarpikEngineRoot = $fullInstallationRoot
    [Environment]::SetEnvironmentVariable(
        "KarpikEngineRoot",
        $fullInstallationRoot,
        [EnvironmentVariableTarget]::User)
}

Export-ModuleMember -Function Set-KarpikEngineRootEnvironment

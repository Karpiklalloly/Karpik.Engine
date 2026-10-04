param(
    [ValidateRange(1, 10)]
    [int] $RepeatCount = 3,
    [string] $ResultsDirectory = 'artifacts/runner-tests'
)

$ErrorActionPreference = 'Stop'
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$stopPath = Join-Path $ResultsDirectory 'stop-sampling'
Remove-Item -LiteralPath $stopPath -ErrorAction SilentlyContinue
$resourcePath = Join-Path $ResultsDirectory 'resources.csv'
$os = Get-CimInstance Win32_OperatingSystem
[pscustomobject]@{
    LogicalProcessors = [Environment]::ProcessorCount
    TotalMemoryMiB = [math]::Round($os.TotalVisibleMemorySize / 1024)
    OS = $os.Caption
    Processor = (Get-CimInstance Win32_Processor).Name -join ', '
} | ConvertTo-Json | Set-Content (Join-Path $ResultsDirectory 'machine.json')

$sampler = Start-Job -ArgumentList $resourcePath, $stopPath -ScriptBlock {
    param($resourcePath, $stopPath)
    $ErrorActionPreference = 'Stop'
    while (-not (Test-Path -LiteralPath $stopPath)) {
        $cpu = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'"
        $memory = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
        $processes = Get-Process
        [pscustomobject]@{
            TimestampUtc = [DateTime]::UtcNow.ToString('o')
            CpuPercent = $cpu.PercentProcessorTime
            AvailableMemoryMiB = $memory.AvailableMBytes
            ProcessCount = $processes.Count
            DotnetProcessCount = @($processes | Where-Object ProcessName -eq 'dotnet').Count
            TesthostProcessCount = @($processes | Where-Object ProcessName -eq 'testhost').Count
        } | Export-Csv -LiteralPath $resourcePath -NoTypeInformation -Append
        Start-Sleep -Seconds 2
    }
}

$failed = $false
try {
    for ($runIndex = 1; $runIndex -le $RepeatCount; $runIndex++) {
        Write-Host "Runner tests: run $runIndex/$RepeatCount"
        & dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj `
            -c Release --no-build --no-restore -m:1 -nr:false `
            --filter 'Category!=Integration' `
            --logger "trx;LogFileName=runner-$runIndex.trx" `
            --logger 'console;verbosity=normal' --results-directory $ResultsDirectory
        if ($LASTEXITCODE -ne 0) {
            $failed = $true
        }
    }
}
finally {
    New-Item -ItemType File -Path $stopPath -Force | Out-Null
    $null = Wait-Job $sampler -Timeout 15
    if ($sampler.State -eq 'Running') {
        Stop-Job $sampler
        $failed = $true
        Write-Warning 'Resource sampler did not stop within 15 seconds.'
    }
    if ($sampler.State -eq 'Failed' -or -not (Test-Path -LiteralPath $resourcePath)) {
        $failed = $true
        Write-Warning 'Resource sampling failed; see the sampler errors below.'
    }
    Receive-Job $sampler -ErrorAction Continue
    Remove-Job $sampler
    Remove-Item -LiteralPath $stopPath
}

if ($failed) {
    exit 1
}

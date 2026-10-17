param([string]$Executable, [string]$OutputDirectory)
. "$PSScriptRoot/Environment.ps1"
if (-not $Executable) { $Executable = Join-Path $workspaceRoot 'artifacts/publish/win-x64/GameDailyHub.App.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspaceRoot 'artifacts/smoke-release' }
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $Executable)) { throw '请先构建或发布应用。' }
$startedAt = Get-Date
$smokeProcess = Start-Process -FilePath $Executable -ArgumentList @('--smoke-root', ('"' + $OutputDirectory + '"')) -PassThru -WindowStyle Hidden
if (-not $smokeProcess.WaitForExit(45000)) {
    $smokeProcess.Kill()
    throw '原生页面冒烟检查超时，已结束本次测试进程。'
}
$reportPath = Join-Path $OutputDirectory 'smoke.json'
if ($smokeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $startedAt) {
    $crash = Join-Path $OutputDirectory 'crash.txt'
    if (Test-Path -LiteralPath $crash) { Get-Content -LiteralPath $crash }
    throw '原生页面冒烟检查失败。'
}
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
if (-not $report.Success -or $report.BindingErrors.Count -ne 0) { throw ($report | ConvertTo-Json -Depth 5) }
$report | ConvertTo-Json

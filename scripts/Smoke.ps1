param([string]$Executable, [string]$OutputDirectory)
. "$PSScriptRoot/Environment.ps1"
if (-not $Executable) {
    $uiTestDirectory = Join-Path $workspaceRoot 'artifacts/ui-test/win-x64/'
    $msbuild = Get-GameDailyHubMSBuild
    Push-Location $workspaceRoot
    try {
        & $msbuild src/GameDailyHub.App/GameDailyHub.App.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 /p:HubUiTest=true "/p:PublishDir=$uiTestDirectory" /m:1 /nodeReuse:false /v:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw '界面测试构建失败。' }
    } finally { Pop-Location }
    $Executable = Join-Path $uiTestDirectory 'GameDailyHub.App.exe'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspaceRoot 'artifacts/smoke-ui-test' }
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $Executable)) { throw '请先构建或发布应用。' }
if ((Get-GameDailyHubExecutionLevel $Executable) -ne 'asInvoker') { throw '冒烟检查仅允许普通权限测试程序；已在启动前拦截。' }
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
if (-not $report.Success -or $report.Elevated -or $report.BindingErrors.Count -ne 0) { throw ($report | ConvertTo-Json -Depth 5) }
$report | ConvertTo-Json

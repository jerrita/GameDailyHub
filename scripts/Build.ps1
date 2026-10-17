param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [switch]$NoRestore)
. "$PSScriptRoot/Environment.ps1"
Push-Location $workspaceRoot
try {
    $msbuild = Get-GameDailyHubMSBuild
    if (-not $NoRestore) {
        & $msbuild GameDailyHub.sln /t:Restore /p:Platform=x64 /p:RestoreLockedMode=true /m:1 /nodeReuse:false /v:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。请检查 NuGet 网络连接及日志。' }
    }
    & $msbuild GameDailyHub.sln "/p:Configuration=$Configuration" /p:Platform=x64 /m:1 /nodeReuse:false /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
} finally { Pop-Location }

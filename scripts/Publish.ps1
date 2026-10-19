param([switch]$NoBuild)
. "$PSScriptRoot/Environment.ps1"
& "$PSScriptRoot/Bundle-Tools.ps1"
if (-not $NoBuild) { & "$PSScriptRoot/Build.ps1" -Configuration Release }
$publishDirectory = Join-Path $workspaceRoot 'artifacts/publish/win-x64/'
$msbuild = Get-GameDailyHubMSBuild
Push-Location $workspaceRoot
try {
    if (Test-Path -LiteralPath $publishDirectory) {
        $resolvedPublishDirectory = (Resolve-Path -LiteralPath $publishDirectory).Path.TrimEnd('\', '/')
        $expectedPublishDirectory = [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'artifacts/publish/win-x64')).TrimEnd('\', '/')
        if ($resolvedPublishDirectory -ne $expectedPublishDirectory) { throw '发布目录超出预期路径，停止清理。' }
        Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
    }
    & $msbuild src/GameDailyHub.App/GameDailyHub.App.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 /p:HubUiTest=false "/p:PublishDir=$publishDirectory" /m:1 /nodeReuse:false /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    if ((Get-GameDailyHubExecutionLevel (Join-Path $publishDirectory 'GameDailyHub.App.exe')) -ne 'requireAdministrator') { throw '发布程序未声明管理员权限。' }
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'artifacts/tools/win-x64') -Destination (Join-Path $publishDirectory 'tools') -Recurse
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'README.md') -Destination $publishDirectory
    $docsDirectory = Join-Path $publishDirectory 'docs'
    New-Item -ItemType Directory -Path $docsDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs/INTEGRATIONS.md') -Destination $docsDirectory
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs/VERIFICATION.md') -Destination $docsDirectory
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs/SETTINGS.md') -Destination $docsDirectory
    Write-Output "发布目录：$publishDirectory"
} finally { Pop-Location }

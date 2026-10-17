param([switch]$NoBuild)
. "$PSScriptRoot/Environment.ps1"
if (-not $NoBuild) { & "$PSScriptRoot/Build.ps1" -Configuration Release }
$publishDirectory = Join-Path $workspaceRoot 'artifacts/publish/win-x64/'
$msbuild = Get-GameDailyHubMSBuild
Push-Location $workspaceRoot
try {
    & $msbuild src/GameDailyHub.App/GameDailyHub.App.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 "/p:PublishDir=$publishDirectory" /m:1 /nodeReuse:false /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'README.md') -Destination $publishDirectory
    $docsDirectory = Join-Path $publishDirectory 'docs'
    New-Item -ItemType Directory -Path $docsDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs/INTEGRATIONS.md') -Destination $docsDirectory
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs/VERIFICATION.md') -Destination $docsDirectory
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs/SETTINGS.md') -Destination $docsDirectory
    $archive = Join-Path $workspaceRoot 'artifacts/GameDailyHub-win-x64.zip'
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -Force
    Write-Output "发布目录：$publishDirectory"
    Write-Output "压缩包：$archive"
} finally { Pop-Location }

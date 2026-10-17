param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [switch]$NoBuild)
. "$PSScriptRoot/Environment.ps1"
if (-not $NoBuild) { & "$PSScriptRoot/Build.ps1" -Configuration $Configuration }
Push-Location $workspaceRoot
try {
    dotnet test tests/GameDailyHub.Tests/GameDailyHub.Tests.csproj --configuration $Configuration --no-build --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/TestResults
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
} finally { Pop-Location }

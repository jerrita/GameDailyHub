$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot 'artifacts/cli'
$env:NUGET_PACKAGES = Join-Path $workspaceRoot '.packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'

function Get-GameDailyHubMSBuild {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw '找不到 vswhere，请安装 Visual Studio 的 WinUI 应用程序开发工作负载。' }
    $installation = & $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $installation) { throw '找不到可用的 Visual Studio MSBuild。' }
    return Join-Path $installation 'MSBuild/Current/Bin/MSBuild.exe'
}

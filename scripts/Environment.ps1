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

function Get-GameDailyHubExecutionLevel([string]$Executable) {
    $manifestTool = Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES 'microsoft.windows.sdk.buildtools') -Filter mt.exe -Recurse |
        Where-Object { $_.FullName -match '\\x64\\mt\.exe$' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $manifestTool) { throw '未找到 Windows SDK manifest 工具，请先还原构建依赖。' }
    $manifestDirectory = Join-Path $workspaceRoot 'artifacts/manifest-check'
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    $manifestPath = Join-Path $manifestDirectory (([guid]::NewGuid().ToString('N')) + '.manifest')
    & $manifestTool.FullName -nologo "-inputresource:$Executable;#1" "-out:$manifestPath" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '无法读取程序内嵌权限清单。' }
    [xml]$manifest = Get-Content -Raw -LiteralPath $manifestPath
    $execution = $manifest.SelectSingleNode('//*[local-name()="requestedExecutionLevel"]')
    if (-not $execution) { throw '程序缺少 requestedExecutionLevel。' }
    return $execution.GetAttribute('level')
}

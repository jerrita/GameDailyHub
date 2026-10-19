. "$PSScriptRoot/Environment.ps1"
$toolsDirectory = Join-Path $workspaceRoot 'artifacts/tools/win-x64'
$downloadDirectory = Join-Path $workspaceRoot 'artifacts/downloads'
New-Item -ItemType Directory -Path $toolsDirectory, $downloadDirectory -Force | Out-Null

$bundles = @(
    @{
        Name = 'uv'; Version = '0.13.1'
        Url = 'https://github.com/astral-sh/uv/releases/download/0.13.1/uv-x86_64-pc-windows-msvc.zip'
        Sha256 = '3b2661a463a5baea7f5076ea2b25ab71d77f36a6c0d809e5350651792f80a672'
        Executable = 'uv.exe'
    }
)
foreach ($bundle in $bundles) {
    $archive = Join-Path $downloadDirectory "$($bundle.Name)-$($bundle.Version)-win-x64.zip"
    if (-not (Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -Uri $bundle.Url -OutFile $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $bundle.Sha256) {
        Remove-Item -LiteralPath $archive -Force
        throw "$($bundle.Name) 下载文件校验失败，请重新发布。"
    }
    $destination = Join-Path $toolsDirectory $bundle.Name
    Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
    if (-not (Test-Path -LiteralPath (Join-Path $destination $bundle.Executable))) {
        throw "$($bundle.Name) 分发包缺少程序文件。"
    }
}
foreach ($license in @('LICENSE-APACHE', 'LICENSE-MIT')) {
    $licensePath = Join-Path $toolsDirectory "uv/$license"
    if (-not (Test-Path -LiteralPath $licensePath)) {
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/astral-sh/uv/0.13.1/$license" -OutFile $licensePath
    }
}

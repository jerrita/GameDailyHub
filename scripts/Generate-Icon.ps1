$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/GameDailyHub.App/Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

# Shared vector geometry keeps the editable SVG and Windows assets in sync.
$tile = 'M 120 32 L 392 32 C 441 32 480 71 480 120 L 480 392 C 480 441 441 480 392 480 L 120 480 C 71 480 32 441 32 392 L 32 120 C 32 71 71 32 120 32 Z'
$pad = 'M 185 164 L 327 164 C 362 164 383 184 395 218 L 423 306 C 436 349 400 377 369 354 L 322 318 L 190 318 L 143 354 C 112 377 76 349 89 306 L 117 218 C 129 184 150 164 185 164 Z'
$cross = 'M 168 211 L 192 211 L 192 235 L 216 235 L 216 259 L 192 259 L 192 283 L 168 283 L 168 259 L 144 259 L 144 235 L 168 235 Z'
$check = 'M 320 361 L 344 385 L 390 337 L 405 352 L 345 415 L 305 376 Z'
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="none">
  <title>GameDailyHub</title>
  <desc>A white game controller on an indigo tile with a mint daily completion badge.</desc>
  <defs><linearGradient id="tile" x1="32" y1="32" x2="480" y2="480" gradientUnits="userSpaceOnUse"><stop stop-color="#4C68EE"/><stop offset="1" stop-color="#6748D5"/></linearGradient></defs>
  <path d="$tile" fill="url(#tile)"/>
  <path d="$pad" fill="#FFFFFF"/>
  <path d="$cross" fill="#5960E4"/>
  <circle cx="330" cy="226" r="15" fill="#5960E4"/>
  <circle cx="364" cy="262" r="15" fill="#5960E4"/>
  <circle cx="355" cy="373" r="72" fill="#6748D5"/>
  <circle cx="355" cy="373" r="61" fill="#69E5BE"/>
  <path d="$check" fill="#173D45"/>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $assetDirectory 'AppIcon.svg'), $svg, [Text.UTF8Encoding]::new($false))

function New-VectorPath([string]$data) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $tokens = $data.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $i = 0; [single]$x = 0; [single]$y = 0
    while ($i -lt $tokens.Length) {
        $command = $tokens[$i++]
        switch ($command) {
            'M' { $x = [single]$tokens[$i++]; $y = [single]$tokens[$i++]; $path.StartFigure() }
            'L' {
                $nextX = [single]$tokens[$i++]; $nextY = [single]$tokens[$i++]
                $path.AddLine($x, $y, $nextX, $nextY); $x = $nextX; $y = $nextY
            }
            'C' {
                $x1 = [single]$tokens[$i++]; $y1 = [single]$tokens[$i++]
                $x2 = [single]$tokens[$i++]; $y2 = [single]$tokens[$i++]
                $nextX = [single]$tokens[$i++]; $nextY = [single]$tokens[$i++]
                $path.AddBezier($x, $y, $x1, $y1, $x2, $y2, $nextX, $nextY); $x = $nextX; $y = $nextY
            }
            'Z' { $path.CloseFigure() }
            default { throw "Unsupported vector command: $command" }
        }
    }
    return ,$path
}

function Get-IconPng([int]$size) {
    $canvas = [Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size * 4 / 512.0, $size * 4 / 512.0)
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Point]::new(32, 32), [Drawing.Point]::new(480, 480),
        [Drawing.ColorTranslator]::FromHtml('#4C68EE'), [Drawing.ColorTranslator]::FromHtml('#6748D5'))
    $path = New-VectorPath $tile
    $graphics.FillPath($gradient, $path); $path.Dispose(); $gradient.Dispose()
    foreach ($shape in @(@($pad, '#FFFFFF'), @($cross, '#5960E4'))) {
        $path = New-VectorPath $shape[0]
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($shape[1]))
        $graphics.FillPath($brush, $path); $path.Dispose(); $brush.Dispose()
    }
    foreach ($circle in @(@(330, 226, 15, '#5960E4'), @(364, 262, 15, '#5960E4'), @(355, 373, 72, '#6748D5'), @(355, 373, 61, '#69E5BE'))) {
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($circle[3]))
        $graphics.FillEllipse($brush, [single]($circle[0] - $circle[2]), [single]($circle[1] - $circle[2]), [single]($circle[2] * 2), [single]($circle[2] * 2))
        $brush.Dispose()
    }
    $path = New-VectorPath $check
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#173D45'))
    $graphics.FillPath($brush, $path); $path.Dispose(); $brush.Dispose(); $graphics.Dispose()
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $output = [Drawing.Graphics]::FromImage($bitmap)
    $output.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $output.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $output.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $output.DrawImage($canvas, 0, 0, $size, $size)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose(); $output.Dispose(); $bitmap.Dispose(); $canvas.Dispose()
    return ,$bytes
}

[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'AppIcon.png'), (Get-IconPng 512))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @($sizes | ForEach-Object { ,(Get-IconPng $_) })
$file = [IO.File]::Create((Join-Path $assetDirectory 'AppIcon.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose() }
Write-Output "Generated SVG, PNG and multi-resolution ICO in $assetDirectory"

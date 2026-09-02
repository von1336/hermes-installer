Add-Type -AssemblyName System.Drawing

$srcPath = "C:\Users\vona\.gemini\antigravity\brain\c53e0638-8f12-4021-a2f4-93b7eda2d505\.user_uploaded\media_1788163277929.jpg"
$outDir = "d:\apk\installer\launcher\Resources"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force }

$outPng = Join-Path $outDir "logo.png"
$outEmblem = Join-Path $outDir "emblem.png"
$outHermesHome = "d:\apk\installer\hermes-logo.png"

$bmp = [System.Drawing.Bitmap]::FromFile($srcPath)
$w = $bmp.Width
$h = $bmp.Height

$result = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

# Background color sample from top-left corner
$bgCorner = $bmp.GetPixel(5, 5)
$bgR = [float]$bgCorner.R
$bgG = [float]$bgCorner.G
$bgB = [float]$bgCorner.B

for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $c = $bmp.GetPixel($x, $y)
        $r = [float]$c.R
        $g = [float]$c.G
        $b = [float]$c.B

        # Euclidean distance from background
        $dist = [Math]::Sqrt(($r - $bgR)*($r - $bgR) + ($g - $bgG)*($g - $bgG) + ($b - $bgB)*($b - $bgB))
        
        # Max possible distance is ~441
        # If very close to background -> transparent
        if ($dist -lt 18.0) {
            $result.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
        }
        elseif ($dist -lt 70.0) {
            # Smooth alpha transition with de-fringing
            $alphaRatio = ($dist - 18.0) / (70.0 - 18.0)
            $alpha = [int](255 * $alphaRatio)
            $alpha = [Math]::Max(0, [Math]::Min(255, $alpha))

            # Unmultiply background color
            $trueR = [Math]::Max(0.0, [Math]::Min(255.0, ($r - (1.0 - $alphaRatio) * $bgR) / $alphaRatio))
            $trueG = [Math]::Max(0.0, [Math]::Min(255.0, ($g - (1.0 - $alphaRatio) * $bgG) / $alphaRatio))
            $trueB = [Math]::Max(0.0, [Math]::Min(255.0, ($b - (1.0 - $alphaRatio) * $bgB) / $alphaRatio))

            $result.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, [int]$trueR, [int]$trueG, [int]$trueB))
        }
        else {
            $result.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $c.R, $c.G, $c.B))
        }
    }
}

$result.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
$result.Save($outHermesHome, [System.Drawing.Imaging.ImageFormat]::Png)

# Crop tight circular emblem for the title bar logo
$emblemW = [int]($w * 0.74)
$emblemH = [int]($h * 0.65)
$cropX = [int](($w - $emblemW) / 2)
$cropY = [int]($h * 0.05)

$emblemRect = New-Object System.Drawing.Rectangle $cropX, $cropY, $emblemW, $emblemH
$emblemBmp = $result.Clone($emblemRect, $result.PixelFormat)
$emblemBmp.Save($outEmblem, [System.Drawing.Imaging.ImageFormat]::Png)

$bmp.Dispose()
$result.Dispose()
$emblemBmp.Dispose()

Write-Host "HIGH QUALITY DE-FRINGED TRANSPARENT LOGOS SAVED!" -ForegroundColor Green

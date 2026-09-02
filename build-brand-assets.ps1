#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$sidePath = Join-Path $assetsDir 'wizard-side.bmp'
$smallPath = Join-Path $assetsDir 'wizard-small.bmp'
$logoPath = Join-Path $assetsDir 'hermes-logo.png'
$iconPath = Join-Path $assetsDir 'hermes-setup.ico'

function New-HermesBrush([string]$hex) {
    $clean = $hex.TrimStart('#')
    $r = [Convert]::ToInt32($clean.Substring(0, 2), 16)
    $g = [Convert]::ToInt32($clean.Substring(2, 2), 16)
    $b = [Convert]::ToInt32($clean.Substring(4, 2), 16)
    return [System.Drawing.Color]::FromArgb(255, $r, $g, $b)
}

function Resolve-LogoSource {
    $workspaceLogo = Join-Path $PSScriptRoot '..\assets\c__Users_vona_AppData_Roaming_Cursor_User_workspaceStorage_1c1a964682c110e7f668c093ae74ab86_images_7b2916bd-27d7-493a-a48c-c7ee9a13773c-fbdbc1a4-e123-40ef-8bf7-701f3098adfa.png'
    $absoluteAttachedLogo = 'C:\Users\vona\.cursor\projects\d-apk/assets/c__Users_vona_AppData_Roaming_Cursor_User_workspaceStorage_1c1a964682c110e7f668c093ae74ab86_images_7b2916bd-27d7-493a-a48c-c7ee9a13773c-fbdbc1a4-e123-40ef-8bf7-701f3098adfa.png'
    $candidates = @(
        $env:HERMES_LOGO_SOURCE,
        $workspaceLogo,
        $absoluteAttachedLogo,
        (Join-Path $assetsDir 'hermes-logo.source.png')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }
    return $null
}

$sourceLogo = Resolve-LogoSource
if ($sourceLogo) {
    # Keep the project logo pinned to the latest provided brand asset.
    Copy-Item -LiteralPath $sourceLogo -Destination $logoPath -Force
} elseif (-not (Test-Path $logoPath)) {
    throw "Hermes logo source not found. Put a logo at '$logoPath' or set HERMES_LOGO_SOURCE."
}

$bgTop = New-HermesBrush '#060914'     # sidebar
$bgBottom = New-HermesBrush '#11182A'  # card
$accent = New-HermesBrush '#6366F1'    # accent
$accentSoft = New-HermesBrush '#818CF8'
$text = New-HermesBrush '#E6EAF2'
$muted = New-HermesBrush '#9AA5BD'

$logo = [System.Drawing.Image]::FromFile($logoPath)

# WizardImageFile (164x314)
$sideBmp = New-Object System.Drawing.Bitmap 164, 314
$g = [System.Drawing.Graphics]::FromImage($sideBmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$rect = New-Object System.Drawing.Rectangle 0, 0, 164, 314
$lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $bgTop, $bgBottom, 90
$g.FillRectangle($lg, $rect)

$accentRail = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, $accent))
$g.FillRectangle($accentRail, 0, 0, 6, 314)

$card = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(34, 230, 234, 242))
$g.FillRectangle($card, 12, 18, 140, 124)

$logoW = 132
$logoH = [int](($logo.Height / [double]$logo.Width) * $logoW)
if ($logoH -gt 118) { $logoH = 118; $logoW = [int](($logo.Width / [double]$logo.Height) * $logoH) }
$logoX = [int]((164 - $logoW) / 2)
$logoY = 22
$g.DrawImage($logo, $logoX, $logoY, $logoW, $logoH)

$fontSub = New-Object System.Drawing.Font 'Segoe UI', 8.5, ([System.Drawing.FontStyle]::Regular)
$brushText = New-Object System.Drawing.SolidBrush $text
$brushMuted = New-Object System.Drawing.SolidBrush $muted
$linePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, $accentSoft), 1)

$g.DrawLine($linePen, 16, 168, 148, 168)
$g.DrawString('Workspace Setup', $fontSub, $brushText, 16, 178)
$g.DrawString('Tailnet-first secure install', $fontSub, $brushMuted, 16, 194)
$g.DrawString('Hermes Professional Installer', $fontSub, $brushMuted, 16, 278)

$sideBmp.Save($sidePath, [System.Drawing.Imaging.ImageFormat]::Bmp)

# WizardSmallImageFile (55x55), crop emblem area from logo
$smallBmp = New-Object System.Drawing.Bitmap 55, 55
$g2 = [System.Drawing.Graphics]::FromImage($smallBmp)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.Clear($bgTop)

$cropX = [int]($logo.Width * 0.20)
$cropY = [int]($logo.Height * 0.08)
$cropW = [int]($logo.Width * 0.60)
$cropH = [int]($logo.Height * 0.60)
$srcRect = New-Object System.Drawing.Rectangle $cropX, $cropY, $cropW, $cropH
$dstRect = New-Object System.Drawing.Rectangle 3, 3, 49, 49
$g2.DrawImage($logo, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$smallBmp.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)

# Setup icon (ICO) derived from the same logo
$iconBmp = New-Object System.Drawing.Bitmap 256, 256
$g3 = [System.Drawing.Graphics]::FromImage($iconBmp)
$g3.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g3.Clear([System.Drawing.Color]::Transparent)
$dstIcon = New-Object System.Drawing.Rectangle 0, 0, 256, 256
$g3.DrawImage($logo, $dstIcon, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$icon = [System.Drawing.Icon]::FromHandle($iconBmp.GetHicon())
$fs = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create)
try {
    $icon.Save($fs)
} finally {
    $fs.Dispose()
    $icon.Dispose()
}

$linePen.Dispose()
$accentRail.Dispose()
$card.Dispose()
$brushText.Dispose()
$brushMuted.Dispose()
$fontSub.Dispose()
$lg.Dispose()
$g.Dispose()
$g2.Dispose()
$g3.Dispose()
$smallBmp.Dispose()
$sideBmp.Dispose()
$iconBmp.Dispose()
$logo.Dispose()

Write-Host "Generated: $sidePath"
Write-Host "Generated: $smallPath"
Write-Host "Generated: $iconPath"
Write-Host "Logo source: $logoPath"

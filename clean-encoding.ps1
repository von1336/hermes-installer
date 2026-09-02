$files = @(
    (Join-Path $PSScriptRoot 'install-hermes.ps1'),
    (Join-Path $PSScriptRoot 'lib\InstallComponents.ps1'),
    (Join-Path $PSScriptRoot 'uninstall-hermes.ps1')
)

$utf8WithBom = New-Object System.Text.UTF8Encoding $true

foreach ($f in $files) {
    if (Test-Path $f) {
        $text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
        
        while ($text.StartsWith("-#Requires") -or $text.StartsWith("?#Requires")) {
            $text = $text.Substring(1)
        }
        
        # Replace unicode dashes with ASCII hyphen
        $dashChars = @(
            [char]0x2014, [char]0x2013, [char]0x2011, [char]0x2012, [char]0x2010, [char]0x2212,
            [char]0x00A0, [char]0x200B
        )
        foreach ($c in $dashChars) {
            $text = $text.Replace($c, [char]0x2D)
        }
        
        $quoteChars = @([char]0x2018, [char]0x2019)
        foreach ($c in $quoteChars) {
            $text = $text.Replace($c, [char]0x27)
        }
        
        $dquoteChars = @([char]0x201C, [char]0x201D)
        foreach ($c in $dquoteChars) {
            $text = $text.Replace($c, [char]0x22)
        }

        # Filter any lingering character >= 128 to hyphen
        $sb = New-Object System.Text.StringBuilder
        foreach ($ch in $text.ToCharArray()) {
            if ([int]$ch -lt 128) {
                [void]$sb.Append($ch)
            } else {
                [void]$sb.Append('-')
            }
        }
        $cleanText = $sb.ToString()

        [System.IO.File]::WriteAllText($f, $cleanText, $utf8WithBom)
        
        $errs = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($cleanText, [ref]$null, [ref]$errs)
        if ($errs -and $errs.Count -gt 0) {
            Write-Host ("ERROR in " + $f + ":") -ForegroundColor Red
            $errs | ForEach-Object { Write-Host $_.ToString() -ForegroundColor Red }
        } else {
            Write-Host ("VALIDATED OK: " + $f) -ForegroundColor Green
        }
    }
}

# ============================================================================
# generer-icone.ps1 : Génère app.ico aux couleurs officielles de Cyber Manager
# ============================================================================
Add-Type -AssemblyName System.Drawing

function Generer-Icone([string]$cheminSortie) {
    $tailles = @(16, 32, 48, 64, 256)
    $images = @()

    foreach ($t in $tailles) {
        $bmp = New-Object System.Drawing.Bitmap $t, $t
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # Fond carré arrondi violet dégradé (#7C3AED -> #4F46E5)
        $rect = New-Object System.Drawing.Rectangle 1, 1, ($t - 2), ($t - 2)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(124, 58, 237),
            [System.Drawing.Color]::FromArgb(79, 70, 229),
            45.0
        )
        
        $rayon = [math]::Max(2, [int]($t * 0.22))
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $rayon * 2
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $g.FillPath($brush, $path)

        # Monogramme "CM" blanc et épuré
        $policeTaille = [math]::Max(7.0, ($t * 0.42))
        $font = New-Object System.Drawing.Font("Segoe UI", $policeTaille, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $brushTexte = [System.Drawing.Brushes]::White
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center

        $rectTexte = New-Object System.Drawing.RectangleF 0, 0, $t, ($t * 1.02)
        $g.DrawString("CM", $font, $brushTexte, $rectTexte, $format)

        $images += $bmp
        $g.Dispose()
    }

    # Écriture du format standard .ICO multi-résolutions
    $stream = [System.IO.File]::OpenWrite($cheminSortie)
    $writer = New-Object System.IO.BinaryWriter $stream

    # ICONDIR
    $writer.Write([uint16]0)      # Réservé
    $writer.Write([uint16]1)      # Type 1 = ICO
    $writer.Write([uint16]$images.Count)

    $offset = 6 + ($images.Count * 16)
    $pngBytesList = @()

    foreach ($img in $images) {
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $pngBytesList += ,$bytes

        $w = if ($img.Width -ge 256) { 0 } else { [byte]$img.Width }
        $h = if ($img.Height -ge 256) { 0 } else { [byte]$img.Height }

        $writer.Write([byte]$w)
        $writer.Write([byte]$h)
        $writer.Write([byte]0)   # Palette
        $writer.Write([byte]0)   # Réservé
        $writer.Write([uint16]1) # Plans de couleur
        $writer.Write([uint16]32)# Bits par pixel
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]$offset)

        $offset += $bytes.Length
    }

    foreach ($bytes in $pngBytesList) {
        $writer.Write($bytes)
    }

    $writer.Flush()
    $writer.Close()
    $stream.Close()
}

$cibleInstaller = Join-Path $PSScriptRoot "app.ico"
$cibleControl = Join-Path (Split-Path -Parent $PSScriptRoot) "CyberManager.Server.Control\app.ico"

Generer-Icone $cibleInstaller
Copy-Item $cibleInstaller $cibleControl -Force
Write-Host "Icône de marque Cyber Manager créée avec succès : $cibleInstaller" -ForegroundColor Green
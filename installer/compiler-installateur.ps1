# Compile CyberManagerServerSetup.exe avec Inno Setup 6 (détection automatique de ISCC.exe).
# Usage : powershell -ExecutionPolicy Bypass -File installer\compiler-installateur.ps1 [-Prod] [-Bundle ...] [-Sortie ...] [-Version ...]
param(
    [string]$Bundle = "",
    [string]$Sortie = "",
    [string]$Version = "",
    [switch]$Prod # Si spécifié : compression maximale pour livraison finale. Par défaut : mode test ultra-rapide (~1 à 2 min).
)

$ErrorActionPreference = "Stop"

$racine = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $PSScriptRoot "CyberManagerServerSetup.iss"
if (-not $Bundle) { $Bundle = Join-Path $racine "build\bundle" }
if (-not $Sortie) { $Sortie = Join-Path $racine "build\installateur" }
if (-not $Version) {
    $fichierVersion = Join-Path $Bundle "VERSION"
    $Version = if (Test-Path $fichierVersion) { (Get-Content $fichierVersion -Raw).Trim() } else { "3.1.0" }
}

function Trouver-Iscc {
    $candidats = @()
    foreach ($cle in @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1")) {
        $p = (Get-ItemProperty $cle -ErrorAction SilentlyContinue).InstallLocation
        if ($p) { $candidats += (Join-Path $p "ISCC.exe") }
    }
    $candidats += "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    $candidats += "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    $candidats += "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    $commande = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($commande) { $candidats += $commande.Source }
    $candidats | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

$iscc = Trouver-Iscc
if (-not $iscc) { throw "Inno Setup 6 est introuvable. Installez-le depuis https://jrsoftware.org/isdl.php (version 6.2 ou plus récente)." }

function Obtenir-VersionInno([string]$cheminIscc) {
    try {
        $vi = (Get-Item $cheminIscc).VersionInfo
        $vStr = if ($vi.ProductVersion -and $vi.ProductVersion -ne "0.0.0.0") { $vi.ProductVersion } else { $vi.FileVersion }
        if ($vStr) {
            $propre = ($vStr -replace '[^\d\.]', '').Trim('.')
            if ($propre -match '^\d+(\.\d+)+$') { return [version]$propre }
        }
    } catch {}

    try {
        $sortie = & $cheminIscc 2>&1 | Out-String
        if ($sortie -match 'Inno Setup (\d+(\.\d+)+)') { return [version]$Matches[1] }
    } catch {}

    return [version]"6.7.3"
}

$versionInno = Obtenir-VersionInno $iscc
Write-Host "Inno Setup détecté (version $versionInno) : $iscc" -ForegroundColor Cyan

# Le script contient des accents : Inno exige un fichier UTF-8 AVEC BOM, sinon il les lit en ANSI.
$octets = [System.IO.File]::ReadAllBytes($iss)
$aBom = ($octets.Length -ge 3 -and $octets[0] -eq 0xEF -and $octets[1] -eq 0xBB -and $octets[2] -eq 0xBF)
if (-not $aBom) {
    $texte = [System.Text.Encoding]::UTF8.GetString($octets)
    if ($texte.Contains([string][char]0xFFFD)) { throw "Le fichier .iss n'est pas en UTF-8 : enregistrez-le en UTF-8 puis relancez." }
    [System.IO.File]::WriteAllText($iss, $texte, (New-Object System.Text.UTF8Encoding $true))
    Write-Host "BOM UTF-8 ajouté au script .iss." -ForegroundColor Yellow
}

foreach ($f in "service\CyberManagerServerService.exe", "control\CyberManagerServer.exe", "installer\CyberManagerSetupTool.exe", "app\artisan", "runtime\php\php.exe", "runtime\caddy\caddy.exe", "VERSION") {
    if (-not (Test-Path (Join-Path $Bundle $f))) { throw "Bundle incomplet : $f est introuvable dans $Bundle (lancez d'abord assembler-bundle.ps1)." }
}

New-Item -ItemType Directory -Path $Sortie -Force | Out-Null
$env:CM_VERSION = $Version
$env:CM_BUNDLE_DIR = $Bundle
$env:CM_OUT_DIR = $Sortie

# Mode de compression : Rapide pour les tests quotidiens, Max pour la production finale
if ($Prod) {
    $env:CM_COMPRESSION = "lzma2/max"
    $env:CM_SOLID = "yes"
    Write-Host "Mode PRODUCTION : compression maximale activée." -ForegroundColor Magenta
} else {
    $env:CM_COMPRESSION = "lzma2/fast"
    $env:CM_SOLID = "no"
    Write-Host "Mode TEST : compilation ultra-rapide (~1 à 2 min)..." -ForegroundColor Yellow
}

Write-Host "Compilation (version $Version)…" -ForegroundColor Cyan
& $iscc "/Q" $iss
if ($LASTEXITCODE -ne 0) { throw "Compilation Inno Setup échouée (code $LASTEXITCODE)." }

$exe = Join-Path $Sortie "CyberManagerServerSetup.exe"
$taille = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "`nInstallateur prêt : $exe ($taille Mo)" -ForegroundColor Green
Write-Host ("SHA-256 : " + (Get-FileHash $exe -Algorithm SHA256).Hash)
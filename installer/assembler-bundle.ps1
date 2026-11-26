# ============================================================================
#  assembler-bundle.ps1
#  Fabrication du bundle autonome (respect strict des Règles 1, 3 et 4)
# ============================================================================
param(
    [string]$Version = "3.1.0",
    [string]$Configuration = "Release",
    [string]$DossierLaravel = "",
    [string]$DossierPhp = "",
    [string]$Prerequis = "",
    [string]$Sortie = ""
)

$ErrorActionPreference = "Stop"

$racineServer = Split-Path -Parent $PSScriptRoot
$racineParente = Split-Path -Parent $racineServer

# Génération préalable de l'icône si absente
$scriptIcone = Join-Path $PSScriptRoot "generer-icone.ps1"
if (Test-Path $scriptIcone) {
    & powershell -ExecutionPolicy Bypass -File $scriptIcone
}

# Résolution automatique du projet Laravel
if (-not $DossierLaravel) {
    $candidatsLaravel = @(
        (Join-Path $racineParente "cyber-manager"),
        (Join-Path $racineServer "..\cyber-manager"),
        (Get-Location).Path
    )
    foreach ($c in $candidatsLaravel) {
        if (Test-Path (Join-Path $c "artisan")) {
            $DossierLaravel = (Resolve-Path $c).Path
            break
        }
    }
}

if (-not $DossierLaravel -or -not (Test-Path (Join-Path $DossierLaravel "artisan"))) {
    throw "Le dossier du projet Laravel est introuvable."
}

if (-not $Sortie) { 
    $Sortie = Join-Path $racineServer "build\bundle" 
}

# Découverte dynamique de PHP
if (-not $DossierPhp) {
    $candidatsPhp = @(
        (Join-Path $DossierLaravel "runtime\php"),
        (Join-Path $racineServer "runtime\php")
    )
    $cmdPhp = Get-Command php.exe -ErrorAction SilentlyContinue
    if ($cmdPhp) { $candidatsPhp += (Split-Path -Parent $cmdPhp.Source) }

    foreach ($drive in (Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3")) {
        $candidatsPhp += (Join-Path $drive.DeviceID "\xampp\php")
        $candidatsPhp += (Join-Path $drive.DeviceID "\php")
    }

    foreach ($rep in $candidatsPhp) {
        if ($rep -and (Test-Path (Join-Path $rep "php.exe")) -and (Test-Path (Join-Path $rep "php-cgi.exe"))) {
            $DossierPhp = (Resolve-Path $rep).Path
            break
        }
    }
}

if (-not $DossierPhp -or -not (Test-Path (Join-Path $DossierPhp "php.exe"))) {
    throw "PHP est introuvable (php.exe et php-cgi.exe requis)."
}

# Découverte dynamique de Caddy
$cheminCaddy = ""
$candidatsCaddy = @(
    (Join-Path $DossierLaravel "runtime\caddy\caddy.exe"),
    (Join-Path $racineServer "runtime\caddy\caddy.exe")
)
$cmdCaddy = Get-Command caddy.exe -ErrorAction SilentlyContinue
if ($cmdCaddy) { $candidatsCaddy += $cmdCaddy.Source }

foreach ($c in $candidatsCaddy) {
    if ($c -and (Test-Path $c)) {
        $cheminCaddy = (Resolve-Path $c).Path
        break
    }
}

if (-not $cheminCaddy) {
    throw "caddy.exe est introuvable."
}

Write-Host "Sources détectées dynamiquement :" -ForegroundColor Cyan
Write-Host "  Laravel : $DossierLaravel"
Write-Host "  PHP     : $DossierPhp"
Write-Host "  Caddy   : $cheminCaddy"
Write-Host "  Cible   : $Sortie`n"

if (Test-Path -LiteralPath $Sortie) { Remove-Item -LiteralPath $Sortie -Recurse -Force }
New-Item -ItemType Directory -Path $Sortie | Out-Null

function Publier([string]$projet, [string]$destination, [string[]]$extra = @()) {
    dotnet publish "$racineServer\$projet" -c $Configuration -r win-x64 --self-contained false -o $destination --nologo -v quiet @extra
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué pour $projet" }
}

Write-Host "1. Compilation du Service Windows" -ForegroundColor Cyan
Publier "CyberManager.Server.Host" (Join-Path $Sortie "service") @("-p:DebugType=None")

Write-Host "2. Compilation de la Télécommande" -ForegroundColor Cyan
Publier "CyberManager.Server.Control" (Join-Path $Sortie "control") @("-p:DebugType=None")

Write-Host "3. Outil d'accompagnement (SetupTool)" -ForegroundColor Cyan
$temp = Join-Path $env:TEMP ("cm-outil-" + [guid]::NewGuid().ToString("N"))
Publier "CyberManager.Server.Setup" $temp @("-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=None")
New-Item -ItemType Directory -Path (Join-Path $Sortie "installer") | Out-Null
Copy-Item (Join-Path $temp "CyberManagerSetupTool.exe") (Join-Path $Sortie "installer\CyberManagerSetupTool.exe")
Remove-Item $temp -Recurse -Force

Write-Host "4. Copie de l'application Laravel" -ForegroundColor Cyan
$appSortie = Join-Path $Sortie "app"
New-Item -ItemType Directory -Path $appSortie | Out-Null
robocopy $DossierLaravel $appSortie /E /NFL /NDL /NJH /NJS /NP /XD ".git" "node_modules" "tests" /XF ".env" "*.sqlite" | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copie de l'application échouée (robocopy $LASTEXITCODE)." }

# Intégration automatique des binaires clients (Installateur, Gardien, Écran)
Write-Host "4.1 Intégration automatique des composants clients" -ForegroundColor Cyan
$dossierCommun = Join-Path $racineParente "cyber-manager-commun"
$cibleInstallateur = Join-Path $appSortie "storage\app\private\installateur"
$ciblePackages = Join-Path $appSortie "storage\app\private\agent-packages"
New-Item -ItemType Directory -Path $cibleInstallateur -Force | Out-Null
New-Item -ItemType Directory -Path $ciblePackages -Force | Out-Null

# CyberManagerInstall.exe
$srcInstall = @(
    (Join-Path $dossierCommun "dist\CyberManagerInstall.exe"),
    (Join-Path $dossierCommun "dist\installateur\CyberManagerInstall.exe"),
    (Join-Path $DossierLaravel "storage\app\private\installateur\CyberManagerInstall.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($srcInstall) {
    Copy-Item $srcInstall (Join-Path $cibleInstallateur "CyberManagerInstall.exe") -Force
    Write-Host "  [+] CyberManagerInstall.exe inclus" -ForegroundColor Green
}

# gardien.zip
$srcGardien = @(
    (Join-Path $dossierCommun "dist\gardien.zip"),
    (Join-Path $DossierLaravel "storage\app\private\installateur\gardien.zip")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($srcGardien) {
    Copy-Item $srcGardien (Join-Path $cibleInstallateur "gardien.zip") -Force
    Write-Host "  [+] gardien.zip inclus" -ForegroundColor Green
}

# Paquets d'écrans de verrouillage
$srcPackages = @(
    (Join-Path $dossierCommun "dist"),
    (Join-Path $DossierLaravel "storage\app\private\agent-packages")
)
foreach ($sp in $srcPackages) {
    if (Test-Path $sp) {
        Get-ChildItem -Path $sp -Filter "ecran-*.zip" | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $ciblePackages $_.Name) -Force
            Write-Host "  [+] $($_.Name) inclus" -ForegroundColor Green
        }
        $manifest = Join-Path $sp "manifest.json"
        if (Test-Path $manifest) {
            Copy-Item $manifest (Join-Path $ciblePackages "manifest.json") -Force
        }
    }
}

Write-Host "5. Intégration du moteur autonome (PHP & Caddy)" -ForegroundColor Cyan
$phpSortie = Join-Path $Sortie "runtime\php"
New-Item -ItemType Directory -Path $phpSortie -Force | Out-Null
robocopy $DossierPhp $phpSortie /E /NFL /NDL /NJH /NJS /NP /XF "*.log" | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copie de PHP échouée (robocopy $LASTEXITCODE)." }

$caddySortie = Join-Path $Sortie "runtime\caddy"
New-Item -ItemType Directory -Path $caddySortie -Force | Out-Null
Copy-Item $cheminCaddy (Join-Path $caddySortie "caddy.exe")

Write-Host "6. Marquage de la version" -ForegroundColor Cyan
[System.IO.File]::WriteAllText((Join-Path $Sortie "VERSION"), $Version, (New-Object System.Text.UTF8Encoding $false))

$taille = [math]::Round(((Get-ChildItem $Sortie -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "`nBundle universel assemblé avec succès : $Sortie ($taille Mo, version $Version)" -ForegroundColor Green
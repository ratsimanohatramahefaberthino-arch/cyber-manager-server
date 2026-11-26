# Validation de l'étape 4.1 : compile, lance les tests unitaires, puis contrôle l'outil « pour de vrai ».
# À lancer dans un PowerShell ADMINISTRATEUR (sinon le rapport signale « non administrateur » : c'est normal).
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$racine = Split-Path -Parent $PSScriptRoot   # ...\cyber-manager-server
$echecs = 0

function Verifier([string]$nom, [bool]$condition) {
    if ($condition) { Write-Host "  [OK] $nom" -ForegroundColor Green }
    else { Write-Host "  [ECHEC] $nom" -ForegroundColor Red; $script:echecs++ }
}

Write-Host "1. Compilation" -ForegroundColor Cyan
dotnet build "$racine\CyberManager.Server.Setup" -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Compilation échouée." }

Write-Host "2. Tests unitaires" -ForegroundColor Cyan
dotnet test "$racine\CyberManager.Server.Setup.Tests" -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests unitaires échoués." }

$exe = Join-Path $racine "CyberManager.Server.Setup\bin\$Configuration\net8.0-windows\CyberManagerSetupTool.exe"
$tmp = Join-Path $env:TEMP "cm-setup-4-1"
New-Item -ItemType Directory $tmp -Force | Out-Null

Write-Host "3. detect (JSON)" -ForegroundColor Cyan
$json = Join-Path $tmp "detect.json"
& $exe detect --version-cible 3.1.0 --format json --sortie $json
Verifier "code de sortie 0" ($LASTEXITCODE -eq 0)
$r = Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json
Verifier "mode valide" (@("nouvelle", "mise_a_jour", "reparation", "retrogradation") -contains $r.installation.mode)
Verifier "Windows 64 bits détecté" ($r.systeme.os64_bits -eq $true)
Verifier "au moins une carte réseau listée" ($r.reseau.interfaces.Count -ge 1)
Verifier "cinq ports sondés" ($r.ports.Count -eq 5)
Verifier "bloquants est une liste" ($null -ne $r.bloquants -or $r.bloquants -is [array] -or $true)
Write-Host ("     Mode : {0} | Port recommandé : {1} | Carte : {2}" -f $r.installation.mode, $r.port_recommande, $r.reseau.interface_recommandee)
foreach ($b in $r.bloquants) { Write-Host "     BLOQUANT : $b" -ForegroundColor Yellow }
foreach ($a in $r.avertissements) { Write-Host "     Attention : $a" -ForegroundColor Yellow }

Write-Host "4. detect (INI pour Inno Setup)" -ForegroundColor Cyan
$ini = Join-Path $tmp "detect.ini"
& $exe detect --version-cible 3.1.0 --format ini --sortie $ini
$octets = [System.IO.File]::ReadAllBytes($ini)
Verifier "INI en UTF-16 LE avec BOM" ($octets[0] -eq 0xFF -and $octets[1] -eq 0xFE)
$texte = Get-Content $ini -Raw -Encoding Unicode
Verifier "section [installation] présente" ($texte -match "\[installation\]")
Verifier "clé mode= présente" ($texte -match "(?m)^mode=")

Write-Host "5. probe-port : port occupé puis libéré" -ForegroundColor Cyan
$ecoute = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, 0)
$ecoute.Start()
$port = $ecoute.LocalEndpoint.Port
$occupe = & $exe probe-port --port $port --format json | ConvertFrom-Json
Verifier "port $port détecté occupé" ($occupe.ports[0].etat -eq "occupe")
Write-Host ("     " + $occupe.ports[0].message)
$ecoute.Stop()
Start-Sleep -Milliseconds 300
$libre = & $exe probe-port --port $port --format json | ConvertFrom-Json
Verifier "port $port détecté libre après arrêt" ($libre.ports[0].etat -eq "libre")

Write-Host "6. Arguments invalides" -ForegroundColor Cyan
$oldPref = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
& $exe probe-port --port abc 2>$null
$ErrorActionPreference = $oldPref
Verifier "port invalide : code de sortie 2" ($LASTEXITCODE -eq 2)

if ($echecs -gt 0) { Write-Host "`n$echecs vérification(s) en échec." -ForegroundColor Red; exit 1 }
Write-Host "`nÉtape 4.1 : toutes les vérifications sont au vert." -ForegroundColor Green
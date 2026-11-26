# Validation de la sous-étape 4.2 en BAC À SABLE : ProgramData simulé dans %TEMP%, aucun service ni règle de pare-feu touché.
# PowerShell ADMINISTRATEUR requis (droits NTFS). Exemple :
#   powershell -ExecutionPolicy Bypass -File installer\valider-4-2.ps1 -App "D:\projets\cyber-manager" -Php "E:\xampp\php\php.exe"
param(
    [Parameter(Mandatory = $true)][string]$App,    # dossier Laravel (contient « artisan » et « vendor »)
    [Parameter(Mandatory = $true)][string]$Php,    # php.exe en ligne de commande, avec pdo_sqlite
    [string]$Configuration = "Release",
    [switch]$Garder
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$racine = Split-Path -Parent $PSScriptRoot    # ...\cyber-manager-server
$echecs = 0

function Verifier([string]$nom, [bool]$condition) {
    if ($condition) { Write-Host "  [OK] $nom" -ForegroundColor Green }
    else { Write-Host "  [ECHEC] $nom" -ForegroundColor Red; $script:echecs++ }
}

function Ecrire-Demande([string]$chemin, [hashtable]$contenu) {
    $json = $contenu | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($chemin, $json, (New-Object System.Text.UTF8Encoding $false))
}

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Lancez ce script dans un PowerShell administrateur." }
if (-not (Test-Path (Join-Path $App "artisan"))) { throw "artisan introuvable dans $App" }
if (-not (Test-Path $Php)) { throw "php.exe introuvable : $Php" }

Write-Host "1. Compilation et tests unitaires C#" -ForegroundColor Cyan
dotnet build "$racine\CyberManager.Server.Setup" -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Compilation échouée." }
dotnet test "$racine\CyberManager.Server.Setup.Tests" -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests C# échoués." }

Write-Host "2. Tests Laravel (serveur:installer)" -ForegroundColor Cyan
Push-Location $App
try {
    & $Php artisan test --filter=ServeurInstallerTest
    Verifier "tests Laravel verts" ($LASTEXITCODE -eq 0)
} finally { Pop-Location }

$exe = Join-Path $racine "CyberManager.Server.Setup\bin\$Configuration\net8.0-windows\CyberManagerSetupTool.exe"
$bac = Join-Path $env:TEMP "cm-bac-a-sable"
if (Test-Path $bac) { Remove-Item $bac -Recurse -Force }
New-Item -ItemType Directory $bac | Out-Null

# Le bac à sable remplace ProgramData, Program Files et le bundle (aucun chemin réel n'est touché)
$env:CYBER_DONNEES_DIR = Join-Path $bac "ProgramData"
$env:CYBER_CODE_DIR    = Join-Path $bac "ProgramFiles"
$env:CYBER_APP_DIR     = $App
$env:CYBER_PHP_CLI     = $Php

$demande  = Join-Path $bac "demande.json"
$rapport1 = Join-Path $bac "rapport1.json"
$mdp      = "Motdepasse-Secret-2026"

Write-Host "3. Première installation (dossiers, configuration, base)" -ForegroundColor Cyan
Ecrire-Demande $demande @{
    mode = "nouvelle"; version_cible = "3.1.0"; nom_cyber = "Cyber Étoile"
    admin_nom = "Rakoto Jean"; admin_email = "gerant@cyber.mg"; mot_de_passe = $mdp
    port_http = 8080
}
& $exe mise-en-place --etape "dossiers,configuration,base" --demande $demande --format json --sortie $rapport1 2>$null
Verifier "code de sortie 0" ($LASTEXITCODE -eq 0)
$r1 = Get-Content $rapport1 -Raw -Encoding UTF8 | ConvertFrom-Json
Verifier "rapport ok" ($r1.ok -eq $true)
Verifier "base créée" ($r1.base -eq "creee")
Verifier "administrateur créé" ($r1.admin -eq "cree")
Verifier "port 8080 retenu" ($r1.port_http -eq 8080)
Verifier "code d'installation au bon format" ($r1.code_installation -match '^[A-Z0-9]{4}-[A-Z0-9]{4}$')
Verifier "fichier de demande effacé (mot de passe non conservé)" (-not (Test-Path $demande))

$env1 = Join-Path $env:CYBER_DONNEES_DIR "config\.env"
$sqlite = Join-Path $env:CYBER_DONNEES_DIR "data\cyber.sqlite"
Verifier ".env créé avec une APP_KEY" ((Get-Content $env1 -Raw) -match "APP_KEY=base64:")
Verifier "cyber.sqlite créée et non vide" ((Test-Path $sqlite) -and ((Get-Item $sqlite).Length -gt 0))
Verifier "version installée enregistrée" ((Get-Content (Join-Path $env:CYBER_DONNEES_DIR "etat\version_installee.txt")).Trim() -eq "3.1.0")

$tout = (Get-ChildItem $bac -Recurse -File -Include *.json, *.log | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Verifier "le mot de passe n'apparaît ni dans le rapport ni dans les journaux" (-not $tout.Contains($mdp))

Write-Host "4. Droits NTFS" -ForegroundColor Cyan
function Sid($acl) { $acl.Access | ForEach-Object { try { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { $_.IdentityReference.Value } } }
$sidsConfig = Sid (Get-Acl (Join-Path $env:CYBER_DONNEES_DIR "config"))
Verifier "config : Utilisateurs sans accès" (-not ($sidsConfig -contains "S-1-5-32-545"))
Verifier "config : Tout le monde sans accès" (-not ($sidsConfig -contains "S-1-1-0"))
Verifier "config : Administrateurs présents" ($sidsConfig -contains "S-1-5-32-544")
Verifier "config : compte du service présent (SID S-1-5-80-…)" (($sidsConfig | Where-Object { $_ -like "S-1-5-80-*" }).Count -ge 1)
$sidsLogs = Sid (Get-Acl (Join-Path $env:CYBER_DONNEES_DIR "logs"))
Verifier "logs : lisibles par les Utilisateurs" ($sidsLogs -contains "S-1-5-32-545")

Write-Host "5. Réparation : rien ne doit être écrasé" -ForegroundColor Cyan
$hashAvant = (Get-FileHash $env1).Hash
$demande2  = Join-Path $bac "demande2.json"
$rapport2  = Join-Path $bac "rapport2.json"
Ecrire-Demande $demande2 @{
    mode = "reparation"; version_cible = "3.1.0"; nom_cyber = "Nom écrasé ?"
    admin_nom = "Intrus"; admin_email = "intrus@cyber.mg"; mot_de_passe = "UnAutreMotDePasse-9"
}
& $exe mise-en-place --etape "dossiers,configuration,base" --demande $demande2 --format json --sortie $rapport2 2>$null
$r2 = Get-Content $rapport2 -Raw -Encoding UTF8 | ConvertFrom-Json
Verifier "rapport ok" ($r2.ok -eq $true)
Verifier "base existante conservée" ($r2.base -eq "existante")
Verifier "administrateur conservé (pas de second compte)" ($r2.admin -eq "conserve")
Verifier ".env strictement identique" ((Get-FileHash $env1).Hash -eq $hashAvant)
Verifier "étape configuration = conserve" (($r2.etapes | Where-Object nom -eq "configuration").statut -eq "conserve")
Verifier "un avertissement signale les infos ignorées" ($r2.avertissements.Count -ge 1)

Write-Host "6. Rapport INI pour Inno Setup" -ForegroundColor Cyan
$ini = Join-Path $bac "rapport.ini"
& $exe mise-en-place --etape dossiers --format ini --sortie $ini 2>$null
$octets = [System.IO.File]::ReadAllBytes($ini)
Verifier "INI en UTF-16 LE avec BOM" ($octets[0] -eq 0xFF -and $octets[1] -eq 0xFE)
Verifier "INI contient [etapes]" ((Get-Content $ini -Raw -Encoding Unicode) -match "\[etapes\]")

Write-Host "7. Erreurs gérées" -ForegroundColor Cyan
$oldPref = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
& $exe mise-en-place --etape magie 2>$null
$ErrorActionPreference = $oldPref
Verifier "étape inconnue : code 2" ($LASTEXITCODE -eq 2)
$demande3 = Join-Path $bac "demande3.json"
Ecrire-Demande $demande3 @{ mode = "nouvelle"; admin_nom = "A"; admin_email = "x"; mot_de_passe = "court" }
& $exe mise-en-place --etape base --demande $demande3 --format json --sortie (Join-Path $bac "rapport3.json") 2>$null
Verifier "demande invalide : code 3 et rien d'exécuté" (($LASTEXITCODE -eq 3) -and ((Get-Content (Join-Path $bac "rapport3.json") -Raw | ConvertFrom-Json).etapes.Count -eq 0))

Remove-Item Env:CYBER_DONNEES_DIR, Env:CYBER_CODE_DIR, Env:CYBER_APP_DIR, Env:CYBER_PHP_CLI -ErrorAction SilentlyContinue
if (-not $Garder) { Remove-Item $bac -Recurse -Force -ErrorAction SilentlyContinue }

if ($echecs -gt 0) { Write-Host "`n$echecs vérification(s) en échec." -ForegroundColor Red; exit 1 }
Write-Host "`nSous-étape 4.2 (bac à sable) : toutes les vérifications sont au vert." -ForegroundColor Green
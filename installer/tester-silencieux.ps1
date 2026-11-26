# Test de bout en bout sur une VM Windows de TEST (installe pour de vrai : service, pare-feu, Program Files, ProgramData).
# PowerShell administrateur. Exemple :
#   powershell -ExecutionPolicy Bypass -File installer\tester-silencieux.ps1 -Installateur "D:\CyberManagerServerSetup.exe"
param(
    [Parameter(Mandatory = $true)][string]$Installateur,
    [int]$Port = 8080,
    [string]$Sauvegarde = ""
)

$ErrorActionPreference = "Stop"
$echecs = 0
$donnees = Join-Path $env:ProgramData "CyberManager"

function Verifier([string]$nom, [bool]$condition) {
    if ($condition) { Write-Host "  [OK] $nom" -ForegroundColor Green }
    else { Write-Host "  [ECHEC] $nom" -ForegroundColor Red; $script:echecs++ }
}

function Installer([string]$journal, [string[]]$extra) {
    $args = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$journal`"") + $extra
    $p = Start-Process -FilePath $Installateur -ArgumentList $args -Wait -PassThru
    return $p.ExitCode
}

function Attendre-Sante([int]$secondes) {
    $limite = (Get-Date).AddSeconds($secondes)
    while ((Get-Date) -lt $limite) {
        try {
            $ports = Get-Content (Join-Path $donnees "runtime\ports.json") -Raw -ErrorAction Stop | ConvertFrom-Json
            $r = Invoke-RestMethod -Uri "http://127.0.0.1:$($ports.http_port)/api/sante" -TimeoutSec 3 -ErrorAction Stop
            if ($r.ok) { return $ports.http_port }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return 0
}

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Lancez ce script dans un PowerShell administrateur." }

$journal1 = Join-Path $env:TEMP "cm-install-1.log"
$params = @("/NomCyber=`"Cyber Étoile`"", "/AdminNom=`"Rakoto Jean`"", "/AdminEmail=gerant@cyber.mg", "/AdminMdp=Motdepasse-Secret-2026", "/Port=$Port", "/Veille=1")
if ($Sauvegarde) { $params += "/Sauvegarde=`"$Sauvegarde`"" }

Write-Host "1. Première installation silencieuse" -ForegroundColor Cyan
$code = Installer $journal1 $params
Verifier "code de sortie 0" ($code -eq 0)
Verifier "fichiers installés (service, télécommande, outil, installateur)" (
    (Test-Path "$env:ProgramFiles\CyberManager\service\CyberManagerServerService.exe") -and
    (Test-Path "$env:ProgramFiles\CyberManager\control\CyberManagerServer.exe") -and
    (Test-Path "$env:ProgramFiles\CyberManager\installer\CyberManagerSetupTool.exe") -and
    (Test-Path "$env:ProgramFiles\CyberManager\installer\CyberManagerServerSetup.exe"))
Verifier "service Windows en marche" ((Get-Service CyberManagerServer).Status -eq "Running")
Verifier "service en démarrage automatique" ((Get-CimInstance Win32_Service -Filter "Name='CyberManagerServer'").StartMode -eq "Auto")
Verifier "règles de pare-feu créées" ((Get-NetFirewallRule -DisplayName "Cyber Manager - *" -ErrorAction SilentlyContinue).Count -eq 2)
Verifier ".env et base créés dans ProgramData" ((Test-Path "$donnees\config\.env") -and (Test-Path "$donnees\data\cyber.sqlite"))
Verifier "lancement avec Windows (HKLM Run)" ($null -ne (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name CyberManagerServer -ErrorAction SilentlyContinue))
$tout = (Get-Content $journal1 -Raw)
Verifier "le mot de passe n'apparaît pas dans le journal de l'installateur" (-not $tout.Contains("Motdepasse-Secret-2026"))

Write-Host "2. Le serveur répond (santé HTTP)" -ForegroundColor Cyan
$portReel = Attendre-Sante 120
Verifier "GET /api/sante répond ok" ($portReel -gt 0)
if ($portReel -gt 0) { Write-Host "     Port HTTP réel : $portReel" }

Write-Host "3. Réparation par-dessus (mode auto, sans données administrateur)" -ForegroundColor Cyan
$hashEnv = (Get-FileHash "$donnees\config\.env").Hash
$code2 = Installer (Join-Path $env:TEMP "cm-install-2.log") @()
Verifier "code de sortie 0" ($code2 -eq 0)
Verifier ".env strictement identique" ((Get-FileHash "$donnees\config\.env").Hash -eq $hashEnv)
Verifier "service toujours en marche" ((Get-Service CyberManagerServer).Status -eq "Running")
Verifier "toujours exactement 2 règles de pare-feu" ((Get-NetFirewallRule -DisplayName "Cyber Manager - *").Count -eq 2)
Verifier "serveur de nouveau en bonne santé" ((Attendre-Sante 120) -gt 0)
Verifier "arrêt propre pendant la réparation : aucun incident de coupure dans le journal du service" (
    -not ((Get-ChildItem "$donnees\logs" -Filter "service-*.log" -ErrorAction SilentlyContinue | Get-Content -ErrorAction SilentlyContinue | Select-String "coupure_serveur|Coupure du serveur")))

if ($echecs -gt 0) { Write-Host "`n$echecs vérification(s) en échec. Journaux : $env:TEMP\cm-install-*.log et $donnees\logs" -ForegroundColor Red; exit 1 }
Write-Host "`nSous-étape 4.3 (VM) : toutes les vérifications sont au vert." -ForegroundColor Green
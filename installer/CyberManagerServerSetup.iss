; ============================================================================
;  CyberManagerServerSetup.iss : assistant d'installation du serveur Cyber Manager
;  Coque graphique Inno Setup 6 (appel de CyberManagerSetupTool.exe)
; ============================================================================

#define VersionEnv GetEnv("CM_VERSION")
#if VersionEnv == ""
  #define AppVersion "3.1.0"
#else
  #define AppVersion VersionEnv
#endif

#define BundleEnv GetEnv("CM_BUNDLE_DIR")
#if BundleEnv == ""
  #define BundleDir SourcePath + "..\build\bundle"
#else
  #define BundleDir BundleEnv
#endif

#define OutEnv GetEnv("CM_OUT_DIR")
#if OutEnv == ""
  #define OutDir SourcePath + "..\build\installateur"
#else
  #define OutDir OutEnv
#endif

; --- Gestion dynamique de la compression (Test rapide vs Production maximale) ---
#define CompEnv GetEnv("CM_COMPRESSION")
#if CompEnv == ""
  #define AppCompression "lzma2/fast"
#else
  #define AppCompression CompEnv
#endif

#define SolidEnv GetEnv("CM_SOLID")
#if SolidEnv == ""
  #define AppSolid "no"
#else
  #define AppSolid SolidEnv
#endif

[Setup]
AppId={{6B3A0E52-7C1D-4E9A-9B46-2F8D5A1C7E30}
AppName=Cyber Manager - Serveur
AppVersion={#AppVersion}
AppVerName=Cyber Manager - Serveur {#AppVersion}
AppPublisher=Cyber Manager
DefaultDirName={autopf}\CyberManager
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DirExistsWarning=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible x64
ArchitecturesInstallIn64BitMode=x64compatible x64
MinVersion=10.0.14393
WizardStyle=modern
WizardSizePercent=120
ShowLanguageDialog=no
LanguageDetectionMethod=none
Compression={#AppCompression}
SolidCompression={#AppSolid}
OutputDir={#OutDir}
OutputBaseFilename=CyberManagerServerSetup
SetupMutex=CyberManagerServerSetupMutex
CloseApplications=no
RestartIfNeededByRun=no
Uninstallable=no
SetupIconFile={#SourcePath}\app.ico

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
Source: "{#SourcePath}\app.ico"; DestDir: "{app}\control"; Flags: ignoreversion
Source: "{#BundleDir}\installer\CyberManagerSetupTool.exe"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#BundleDir}\installer\CyberManagerSetupTool.exe"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "{srcexe}"; DestDir: "{app}\installer"; DestName: "CyberManagerServerSetup.exe"; Flags: external ignoreversion; Check: CopierInstallateur

Source: "{#BundleDir}\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\control\*"; DestDir: "{app}\control"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\runtime\*"; DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\VERSION"; DestDir: "{app}"; Flags: ignoreversion

; Déploiement direct des paquets d'installation clients dans ProgramData (Règle 2 : Zéro configuration)
Source: "{#BundleDir}\app\storage\app\private\*"; DestDir: "{commonappdata}\CyberManager\storage\app\private"; Flags: ignoreversion recursesubdirs createallsubdirs onlyifdoesntexist

[InstallDelete]
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\runtime"
Type: filesandordirs; Name: "{app}\service"
Type: filesandordirs; Name: "{app}\control"

[Icons]
Name: "{commonprograms}\Cyber Manager - Serveur"; Filename: "{app}\control\CyberManagerServer.exe"; IconFilename: "{app}\control\app.ico"; Comment: "Télécommande du serveur Cyber Manager"
Name: "{commondesktop}\Cyber Manager - Serveur"; Filename: "{app}\control\CyberManagerServer.exe"; IconFilename: "{app}\control\app.ico"; Comment: "Télécommande du serveur Cyber Manager"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CyberManagerServer"; ValueData: """{app}\control\CyberManagerServer.exe"" --minimise"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\control\CyberManagerServer.exe"; Description: "Lancer la télécommande du serveur"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: InstallationReussie
Filename: "{code:UrlServeur}"; Description: "Ouvrir Cyber Manager dans le navigateur"; Flags: shellexec postinstall skipifsilent runasoriginaluser; Check: InstallationReussie
Filename: "{sys}\cmd.exe"; Parameters: "/C ""<nul set /p={code:CodeInstallation}| clip"""; Description: "Copier le code d'installation des postes dans le presse-papiers"; Flags: runhidden postinstall skipifsilent runasoriginaluser unchecked; Check: CodeDisponible

[Code]
function SetTimer(hWnd: LongWord; nIDEvent, uElapse: LongWord; lpTimerFunc: LongWord): LongWord;
  external 'SetTimer@user32.dll stdcall';

function KillTimer(hWnd: LongWord; uIDEvent: LongWord): LongWord;
  external 'KillTimer@user32.dll stdcall';

function MultiByteToWideChar(CodePage: LongWord; dwFlags: LongWord; lpMultiByteStr: AnsiString;
  cchMultiByte: Integer; lpWideCharStr: String; cchWideChar: Integer): Integer;
  external 'MultiByteToWideChar@kernel32.dll stdcall';

const
  CRLF    = #13#10;
  CVert   = $3A7B1E;
  COrange = $006BB3;
  CRouge  = $1823B4;
  CGris   = $6B6B6B;
  CBleu   = $B16F1F;
  CTexte  = $333333;
  CleRuntimes = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\';

var
  OutilChemin: String;
  DetectIni, RapportIni: TStringList;
  DernierMessage: String;

  Mode: String;
  EstNouvelle, DonneesExistantes, ServiceExiste: Boolean;
  BloquantsGlobal, DotNetOk, VcOk: Boolean;
  SessionsEnCours: Integer;
  PortRecommande, PortChoisi: Integer;
  PortLibre: Boolean;
  MessagePortAuto: String;

  InstallEchec: Boolean;
  CodeInstall, JournalChemin, AvertissementsFin: String;
  AvertCount: Integer;

  PageVerif, PageAdmin, PageReseau: TWizardPage;
  PageSauvegarde: TInputDirWizardPage;
  VerifRempli: Boolean;
  VerifY: Integer;

  EditNomCyber, EditAdminNom, EditEmail: TEdit;
  EditMdp, EditMdp2: TPasswordEdit;
  LblForce, LblConfirme: TNewStaticText;

  ComboCarte: TNewComboBox;
  IpCartes, NomsCartes: array of String;
  LblAdresse, LblPastille, LblPastilleTexte: TNewStaticText;
  RadioAuto, RadioManuel: TNewRadioButton;
  EditPort: TEdit;
  CocheVeille: TNewCheckBox;

  TimerId, TimerCallback: LongWord;

function JsonTexte(S: String): String;
begin
  StringChangeEx(S, '\', '\\', True);
  StringChangeEx(S, '"', '\"', True);
  StringChangeEx(S, #13, ' ', True);
  StringChangeEx(S, #10, ' ', True);
  StringChangeEx(S, #9, ' ', True);
  Result := '"' + S + '"';
end;

function BoolJson(Valeur: Boolean): String;
begin
  if Valeur then Result := 'true' else Result := 'false';
end;

function Utf8VersString(const S: AnsiString): String;
var
  Taille: Integer;
begin
  Result := '';
  if Length(S) = 0 then Exit;

  Taille := MultiByteToWideChar(65001, 0, S, Length(S), '', 0);
  if Taille <= 0 then
  begin
    Result := String(S);
    Exit;
  end;

  SetLength(Result, Taille);
  MultiByteToWideChar(65001, 0, S, Length(S), Result, Taille);
end;

procedure ChargerIni(const Chemin: String; Ini: TStringList);
var
  Brut: AnsiString;
  Texte, Ligne, Section: String;
  P: Integer;
begin
  Ini.Clear;
  if not LoadStringFromFile(Chemin, Brut) then Exit;

  if Length(Brut) >= 3 then
    if (Ord(Brut[1]) = $EF) and (Ord(Brut[2]) = $BB) and (Ord(Brut[3]) = $BF) then
      Delete(Brut, 1, 3);

  Texte := Utf8VersString(Brut);
  Section := '';

  while Length(Texte) > 0 do
  begin
    P := Pos(#10, Texte);
    if P = 0 then
    begin
      Ligne := Texte;
      Texte := '';
    end
    else
    begin
      Ligne := Copy(Texte, 1, P - 1);
      Delete(Texte, 1, P);
    end;

    Ligne := Trim(Ligne);
    if Length(Ligne) = 0 then Continue;

    if Ligne[1] = '[' then
      Section := Copy(Ligne, 2, Length(Ligne) - 2)
    else
    begin
      P := Pos('=', Ligne);
      if P > 1 then
        Ini.Add(Section + '|' + Copy(Ligne, 1, P - 1) + #1 + Copy(Ligne, P + 1, Length(Ligne)));
    end;
  end;
end;

function IniGet(Ini: TStringList; const Section, Cle, Defaut: String): String;
var
  I, L: Integer;
  Prefixe: String;
begin
  Result := Defaut;
  Prefixe := Section + '|' + Cle + #1;
  L := Length(Prefixe);

  for I := 0 to Ini.Count - 1 do
  begin
    if Copy(Ini.Strings[I], 1, L) = Prefixe then
    begin
      Result := Copy(Ini.Strings[I], L + 1, Length(Ini.Strings[I]));
      Exit;
    end;
  end;
end;

function IniInt(Ini: TStringList; const Section, Cle: String; Defaut: Integer): Integer;
begin
  Result := StrToIntDef(IniGet(Ini, Section, Cle, ''), Defaut);
end;

function NouvelleEtiquette(Page: TWizardPage; const Texte: String; Gauche, Haut, Largeur: Integer; Gras: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(Page);
  Result.Parent := Page.Surface;
  Result.Left := Gauche;
  Result.Top := Haut;
  Result.Width := Largeur;
  Result.WordWrap := True;
  if Gras then Result.Font.Style := [fsBold];
  Result.Caption := Texte;
  Result.AutoSize := True;
end;

function ValNomCyber: String;
begin
  if WizardSilent then Result := Trim(ExpandConstant('{param:NomCyber|Mon Cyber}'))
  else Result := Trim(EditNomCyber.Text);
end;

function ValAdminNom: String;
begin
  if WizardSilent then Result := Trim(ExpandConstant('{param:AdminNom|}'))
  else Result := Trim(EditAdminNom.Text);
end;

function ValAdminEmail: String;
begin
  if WizardSilent then Result := Trim(ExpandConstant('{param:AdminEmail|}'))
  else Result := Trim(EditEmail.Text);
end;

function ValAdminMdp: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:AdminMdp|}')
  else Result := EditMdp.Text;
end;

function ValVeille: Boolean;
begin
  if WizardSilent then Result := (ExpandConstant('{param:Veille|1}') = '1')
  else Result := CocheVeille.Checked;
end;

function ValSauvegarde: String;
begin
  if WizardSilent then Result := Trim(ExpandConstant('{param:Sauvegarde|}'))
  else Result := Trim(PageSauvegarde.Values[0]);

  if Result = '' then Result := ExpandConstant('{commonappdata}\CyberManager\sauvegardes');
end;

function ValInterface: String;
var
  Index: Integer;
begin
  Result := '';
  if WizardSilent then
    Result := IniGet(DetectIni, 'reseau', 'interface_recommandee', '')
  else
  begin
    Index := ComboCarte.ItemIndex;
    if (Index >= 0) and (Index < GetArrayLength(NomsCartes)) then Result := NomsCartes[Index];
  end;
end;

function ValPort: Integer;
begin
  if WizardSilent then
  begin
    Result := StrToIntDef(ExpandConstant('{param:Port|0}'), 0);
    if Result = 0 then Result := PortRecommande;
  end
  else
    Result := PortChoisi;
end;

function DossierRuntimeExiste(const Nom: String): Boolean;
var
  FindRec: TFindRec;
  DossierShared, NomDossier: String;
  P, Majeur: Integer;
begin
  Result := False;
  DossierShared := ExpandConstant('{commonpf64}\dotnet\shared\') + Nom;
  
  if FindFirst(DossierShared + '\*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and 16) <> 0) and 
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          NomDossier := FindRec.Name;
          P := Pos('.', NomDossier);
          if P > 1 then
          begin
            Majeur := StrToIntDef(Copy(NomDossier, 1, P - 1), 0);
            if Majeur >= 8 then
            begin
              Result := True;
              Break;
            end;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function RuntimeInstalle(const Nom: String): Boolean;
var
  Versions: TArrayOfString;
  I, P, Majeur: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, CleRuntimes + Nom, Versions) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
    begin
      P := Pos('.', Versions[I]);
      if P > 1 then
      begin
        Majeur := StrToIntDef(Copy(Versions[I], 1, P - 1), 0);
        if Majeur >= 8 then Result := True;
      end;
    end;
  end;
  
  if not Result then
    Result := DossierRuntimeExiste(Nom);
end;

function DotNetPresent: Boolean;
begin
  Result := (RuntimeInstalle('Microsoft.NETCore.App') and RuntimeInstalle('Microsoft.WindowsDesktop.App'))
            or (FileExists(ExpandConstant('{commonpf64}\dotnet\dotnet.exe')) and DossierRuntimeExiste('Microsoft.WindowsDesktop.App'));
end;

function VcRedistPresent: Boolean;
var
  Installe: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installe) then
    Result := (Installe = 1);
end;

procedure EcrireDemande(const Chemin: String; AvecSecret: Boolean);
var
  J: String;
  Lignes: TArrayOfString;
begin
  J := '{"v":1,"version_cible":' + JsonTexte('{#AppVersion}') + ',"mode":' + JsonTexte(Mode);
  J := J + ',"empecher_veille":' + BoolJson(EstNouvelle and ValVeille);

  if EstNouvelle then
  begin
    J := J + ',"nom_cyber":' + JsonTexte(ValNomCyber);

    if not DonneesExistantes then
    begin
      if AvecSecret then
      begin
        J := J + ',"admin_nom":' + JsonTexte(ValAdminNom);
        J := J + ',"admin_email":' + JsonTexte(ValAdminEmail);
        J := J + ',"mot_de_passe":' + JsonTexte(ValAdminMdp);
      end;
    end;

    if ValPort > 0 then J := J + ',"port_http":' + IntToStr(ValPort);

    J := J + ',"interface":' + JsonTexte(ValInterface);
    J := J + ',"dossier_sauvegarde":' + JsonTexte(ValSauvegarde);
  end;

  J := J + '}';

  SetArrayLength(Lignes, 1);
  Lignes[0] := J;
  SaveStringsToUTF8File(Chemin, Lignes, False);
end;

procedure CollecterRapport;
var
  Valeur: String;
  I, N: Integer;
begin
  Valeur := IniGet(RapportIni, 'meta', 'code_installation', '');
  if Valeur <> '' then CodeInstall := Valeur;

  Valeur := IniGet(RapportIni, 'meta', 'journal', '');
  if Valeur <> '' then JournalChemin := Valeur;

  N := IniInt(RapportIni, 'avertissements', 'count', 0);
  for I := 1 to N do
  begin
    if AvertCount < 5 then
    begin
      AvertissementsFin := AvertissementsFin + '• ' + IniGet(RapportIni, 'avertissements', IntToStr(I), '') + CRLF;
      AvertCount := AvertCount + 1;
    end;
  end;
end;

function ExecuterEtape(const Etape: String; AvecSecret: Boolean): Boolean;
var
  Demande, Rapport, Params, Msg: String;
  Code: Integer;
begin
  Result := False;
  DernierMessage := '';

  Demande := ExpandConstant('{tmp}\demande.json');
  Rapport := ExpandConstant('{tmp}\rapport-' + Etape + '.ini');
  DeleteFile(Rapport);
  EcrireDemande(Demande, AvecSecret);

  Params := 'mise-en-place --etape ' + Etape + ' --demande ' + AddQuotes(Demande) +
            ' --format ini --encodage utf8 --sortie ' + AddQuotes(Rapport);

  if not Exec(OutilChemin, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    DeleteFile(Demande);
    DernierMessage := 'Le programme d''installation n''a pas pu démarrer.';
    Exit;
  end;

  DeleteFile(Demande);
  ChargerIni(Rapport, RapportIni);

  if RapportIni.Count = 0 then
  begin
    DernierMessage := 'Aucun rapport reçu du programme d''installation (code ' + IntToStr(Code) + ').';
    Exit;
  end;

  CollecterRapport;
  Result := (IniGet(RapportIni, 'meta', 'ok', '0') = '1');

  if not Result then
  begin
    Msg := IniGet(RapportIni, 'erreurs', '1', '');
    if Msg = '' then Msg := 'L''étape « ' + Etape + ' » a échoué (code ' + IntToStr(Code) + ').';
    DernierMessage := Msg;
  end;
end;

function LibelleEtape(const Nom: String): String;
begin
  if Nom = 'dossiers' then Result := 'Préparation des dossiers et des droits d''accès…'
  else if Nom = 'configuration' then Result := 'Création de la configuration du serveur…'
  else if Nom = 'taches' then Result := 'Nettoyage des anciennes tâches planifiées…'
  else if Nom = 'parefeu' then Result := 'Réglage du pare-feu Windows (réseau local uniquement)…'
  else if Nom = 'base' then Result := 'Préparation de la base de données et du compte administrateur…'
  else if Nom = 'sauvegarde' then Result := 'Préparation du dossier de sauvegarde…'
  else if Nom = 'service' then Result := 'Enregistrement du service Windows…'
  else if Nom = 'veille' then Result := 'Désactivation de la mise en veille…'
  else if Nom = 'demarrer' then Result := 'Démarrage du serveur…'
  else Result := Nom;
end;

function AdresseServeur: String;
var
  Hote: String;
  Port, Index: Integer;
begin
  Hote := GetComputerNameString;

  if not WizardSilent then
  begin
    Index := ComboCarte.ItemIndex;
    if (Index >= 0) and (Index < GetArrayLength(IpCartes)) then
      if IpCartes[Index] <> '' then Hote := IpCartes[Index];
  end
  else if IniGet(DetectIni, 'reseau', 'adresse_recommandee', '') <> '' then
    Hote := IniGet(DetectIni, 'reseau', 'adresse_recommandee', '');

  Port := ValPort;
  if Port = 0 then Port := PortRecommande;
  if Port = 0 then Port := 80;

  if Port = 80 then Result := 'http://' + Hote
  else Result := 'http://' + Hote + ':' + IntToStr(Port);
end;

function UrlServeur(Param: String): String;
begin
  Result := AdresseServeur;
end;

function CodeInstallation(Param: String): String;
begin
  Result := CodeInstall;
end;

function CodeDisponible: Boolean;
begin
  Result := (not InstallEchec) and (CodeInstall <> '');
end;

function InstallationReussie: Boolean;
begin
  Result := not InstallEchec;
end;

function CopierInstallateur: Boolean;
begin
  Result := (CompareText(ExtractFileDir(ExpandConstant('{srcexe}')), ExpandConstant('{app}\installer')) <> 0);
end;

procedure AjouterVerif(const Texte: String; Couleur: Integer);
var
  Pastille, Ligne: TNewStaticText;
begin
  Pastille := NouvelleEtiquette(PageVerif, '●', 0, VerifY, ScaleX(16), False);
  Pastille.Font.Color := Couleur;

  Ligne := NouvelleEtiquette(PageVerif, Texte, ScaleX(18), VerifY, PageVerif.SurfaceWidth - ScaleX(18), False);
  Ligne.Font.Color := CTexte;

  VerifY := VerifY + Ligne.Height + ScaleY(6);
end;

procedure RemplirVerifications;
var
  I, N, Mo: Integer;
  Message: String;
begin
  if VerifRempli then Exit;
  VerifRempli := True;
  VerifY := 0;

  if IniGet(DetectIni, 'systeme', 'supporte', '0') = '1' then
    AjouterVerif(IniGet(DetectIni, 'systeme', 'nom_windows', 'Windows') + ' (64 bits) : compatible.', CVert)
  else
    AjouterVerif('Cette version de Windows n''est pas prise en charge (Windows 10 64 bits ou plus récent requis).', CRouge);

  Mo := IniInt(DetectIni, 'systeme', 'espace_libre_code_mo', 0);
  if Mo >= 3072 then
    AjouterVerif('Espace disque : ' + IntToStr(Mo div 1024) + ' Go disponibles.', CVert)
  else if Mo >= 1024 then
    AjouterVerif('Espace disque : seulement ' + IntToStr(Mo div 1024) + ' Go disponibles.', COrange)
  else if Mo > 0 then
    AjouterVerif('Espace disque insuffisant : ' + IntToStr(Mo) + ' Mo disponibles.', CRouge);

  if DotNetOk then AjouterVerif('Composant .NET : présent.', CVert);

  if VcOk then
    AjouterVerif('Composant Visual C++ : présent.', CVert)
  else
    AjouterVerif('Composant Visual C++ : absent (installez Microsoft Visual C++ Redistributable x64).', CRouge);

  Message := IniGet(DetectIni, 'installation', 'raison', '');
  if Message <> '' then
  begin
    if Mode = 'retrogradation' then AjouterVerif(Message, CRouge)
    else AjouterVerif(Message, CVert);
  end;

  if IniGet(DetectIni, 'reseau', 'interface_recommandee', '') <> '' then
    AjouterVerif('Réseau : carte « ' + IniGet(DetectIni, 'reseau', 'interface_recommandee', '') + ' » (' +
                 IniGet(DetectIni, 'reseau', 'adresse_recommandee', '') + ').', CVert)
  else
    AjouterVerif('Aucune carte réseau connectée trouvée : branchez le câble réseau.', COrange);

  Message := IniGet(DetectIni, 'ports', '1_message', '');
  if Message <> '' then
  begin
    if IniGet(DetectIni, 'ports', '1_etat', '') = 'libre' then AjouterVerif(Message, CVert)
    else AjouterVerif(Message + ' Un autre port sera proposé.', COrange);
  end;

  N := IniInt(DetectIni, 'bloquants', 'count', 0);
  for I := 1 to N do
    AjouterVerif(IniGet(DetectIni, 'bloquants', IntToStr(I), ''), CRouge);

  N := IniInt(DetectIni, 'avertissements', 'count', 0);
  for I := 1 to N do
  begin
    if I <= 5 then
      AjouterVerif(IniGet(DetectIni, 'avertissements', IntToStr(I), ''), COrange);
  end;

  if BloquantsGlobal then
    PageVerif.Description := 'Corrigez les points en rouge, puis relancez l''installation.'
  else
    PageVerif.Description := 'Tout est prêt : rien ne vous est demandé sur cet écran.';
end;

function ValiderVerif: Boolean;
begin
  Result := True;

  if BloquantsGlobal then
  begin
    MsgBox('L''installation ne peut pas continuer : corrigez d''abord les points en rouge.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (not EstNouvelle) and (SessionsEnCours > 0) then
  begin
    Result := (MsgBox(IntToStr(SessionsEnCours) + ' session(s) payante(s) sont en cours.' + CRLF + CRLF +
      'Pendant l''opération elles seront figées : le temps d''arrêt ne sera pas facturé.' + CRLF + CRLF +
      'Voulez-vous continuer maintenant ?', mbConfirmation, MB_YESNO) = IDYES);
  end;
end;

procedure CreerPageVerif;
begin
  PageVerif := CreateCustomPage(wpWelcome, 'Vérification de votre ordinateur',
    'L''assistant contrôle que tout est en ordre avant de commencer.');
  VerifRempli := False;
end;

function ForceMdp(const S: String): Integer;
var
  I, C: Integer;
  Minus, Majus, Chiffre, Symbole: Boolean;
begin
  Result := 0;
  if Length(S) = 0 then Exit;

  Minus := False; Majus := False; Chiffre := False; Symbole := False;

  for I := 1 to Length(S) do
  begin
    C := Ord(S[I]);
    if (C >= 97) and (C <= 122) then Minus := True
    else if (C >= 65) and (C <= 90) then Majus := True
    else if (C >= 48) and (C <= 57) then Chiffre := True
    else Symbole := True;
  end;

  if Length(S) >= 8 then Result := Result + 1;
  if Length(S) >= 12 then Result := Result + 1;
  if Minus and Majus then Result := Result + 1;
  if Chiffre then Result := Result + 1;
  if Symbole then Result := Result + 1;
end;

function EmailPlausible(const E: String): Boolean;
var
  P: Integer;
  Domaine: String;
begin
  Result := False;
  if (Length(E) < 5) or (Length(E) > 190) then Exit;
  if Pos(' ', E) > 0 then Exit;

  P := Pos('@', E);
  if P < 2 then Exit;

  Domaine := Copy(E, P + 1, Length(E));
  if Pos('@', Domaine) > 0 then Exit;
  if Pos('.', Domaine) < 2 then Exit;
  if Domaine[Length(Domaine)] = '.' then Exit;

  Result := True;
end;

procedure MdpChange(Sender: TObject);
var
  Score: Integer;
begin
  Score := ForceMdp(EditMdp.Text);

  if Length(EditMdp.Text) = 0 then
  begin
    LblForce.Caption := 'Au moins 8 caractères. Mélangez lettres, chiffres et symboles.';
    LblForce.Font.Color := CGris;
  end
  else if Length(EditMdp.Text) < 8 then
  begin
    LblForce.Caption := 'Trop court : 8 caractères au minimum.';
    LblForce.Font.Color := CRouge;
  end
  else if Score <= 2 then
  begin
    LblForce.Caption := 'Mot de passe faible : ajoutez des chiffres ou des symboles.';
    LblForce.Font.Color := COrange;
  end
  else if Score <= 4 then
  begin
    LblForce.Caption := 'Mot de passe correct.';
    LblForce.Font.Color := CVert;
  end
  else
  begin
    LblForce.Caption := 'Mot de passe fort.';
    LblForce.Font.Color := CVert;
  end;

  if (Length(EditMdp2.Text) > 0) and (EditMdp.Text <> EditMdp2.Text) then
  begin
    LblConfirme.Caption := 'Les deux mots de passe ne sont pas identiques.';
    LblConfirme.Font.Color := COrange;
  end
  else if Length(EditMdp2.Text) > 0 then
  begin
    LblConfirme.Caption := 'Les deux mots de passe sont identiques.';
    LblConfirme.Font.Color := CVert;
  end
  else
    LblConfirme.Caption := '';
end;

function ValiderAdmin: Boolean;
var
  Msg: String;
begin
  Result := False;
  Msg := '';

  if (Length(Trim(EditNomCyber.Text)) < 1) or (Length(Trim(EditNomCyber.Text)) > 80) then
    Msg := 'Indiquez le nom de votre cyber (80 caractères maximum).'
  else if Length(Trim(EditAdminNom.Text)) < 2 then
    Msg := 'Indiquez le nom de l''administrateur (2 caractères minimum).'
  else if not EmailPlausible(Trim(EditEmail.Text)) then
    Msg := 'L''adresse e-mail n''est pas valide.'
  else if Length(EditMdp.Text) < 8 then
    Msg := 'Le mot de passe doit comporter au moins 8 caractères.'
  else if EditMdp.Text <> EditMdp2.Text then
    Msg := 'Les deux mots de passe ne sont pas identiques.';

  if Msg <> '' then
    MsgBox(Msg, mbError, MB_OK)
  else
    Result := True;
end;

procedure CreerPageAdmin;
var
  L: Integer;
begin
  PageAdmin := CreateCustomPage(PageVerif.ID, 'Votre cyber et son administrateur',
    'Ces informations servent à créer le compte administrateur initial.');
  L := PageAdmin.SurfaceWidth;

  NouvelleEtiquette(PageAdmin, 'Nom de votre cyber', 0, 0, L, False);
  EditNomCyber := TEdit.Create(PageAdmin);
  EditNomCyber.Parent := PageAdmin.Surface;
  EditNomCyber.Left := 0; EditNomCyber.Top := ScaleY(18); EditNomCyber.Width := L;
  EditNomCyber.MaxLength := 80;
  EditNomCyber.Text := 'Mon Cyber';

  NouvelleEtiquette(PageAdmin, 'Nom de l''administrateur', 0, ScaleY(50), L, False);
  EditAdminNom := TEdit.Create(PageAdmin);
  EditAdminNom.Parent := PageAdmin.Surface;
  EditAdminNom.Left := 0; EditAdminNom.Top := ScaleY(68); EditAdminNom.Width := L;
  EditAdminNom.MaxLength := 120;

  NouvelleEtiquette(PageAdmin, 'Adresse e-mail (identifiant de connexion)', 0, ScaleY(100), L, False);
  EditEmail := TEdit.Create(PageAdmin);
  EditEmail.Parent := PageAdmin.Surface;
  EditEmail.Left := 0; EditEmail.Top := ScaleY(118); EditEmail.Width := L;
  EditEmail.MaxLength := 190;

  NouvelleEtiquette(PageAdmin, 'Mot de passe', 0, ScaleY(150), L div 2 - ScaleX(8), False);
  EditMdp := TPasswordEdit.Create(PageAdmin);
  EditMdp.Parent := PageAdmin.Surface;
  EditMdp.Left := 0; EditMdp.Top := ScaleY(168); EditMdp.Width := L div 2 - ScaleX(8);
  EditMdp.MaxLength := 128;
  EditMdp.OnChange := @MdpChange;

  NouvelleEtiquette(PageAdmin, 'Confirmer le mot de passe', L div 2 + ScaleX(8), ScaleY(150), L div 2 - ScaleX(8), False);
  EditMdp2 := TPasswordEdit.Create(PageAdmin);
  EditMdp2.Parent := PageAdmin.Surface;
  EditMdp2.Left := L div 2 + ScaleX(8); EditMdp2.Top := ScaleY(168); EditMdp2.Width := L div 2 - ScaleX(8);
  EditMdp2.MaxLength := 128;
  EditMdp2.OnChange := @MdpChange;

  LblForce := NouvelleEtiquette(PageAdmin, '', 0, ScaleY(198), L, False);
  LblConfirme := NouvelleEtiquette(PageAdmin, '', 0, ScaleY(216), L, False);
  MdpChange(nil);
end;

procedure ArreterSonde;
begin
  if TimerId <> 0 then
  begin
    KillTimer(0, TimerId);
    TimerId := 0;
  end;
end;

procedure MontrerPastille(Couleur: Integer; const Texte: String);
begin
  LblPastille.Font.Color := Couleur;
  LblPastilleTexte.Caption := Texte;
  LblPastille.Update;
  LblPastilleTexte.Update;
end;

procedure ActualiserAdresse;
begin
  LblAdresse.Caption := AdresseServeur;
end;

procedure AppliquerBoutonSuivant;
begin
  if WizardForm.CurPageID = PageReseau.ID then
    WizardForm.NextButton.Enabled := PortLibre;
end;

procedure ActualiserSonde;
var
  Port, Code: Integer;
  Rapport, Etat, Msg: String;
begin
  ArreterSonde;

  if RadioAuto.Checked then
  begin
    if PortRecommande > 0 then
    begin
      PortChoisi := PortRecommande;
      PortLibre := True;
      MontrerPastille(CVert, MessagePortAuto);
    end
    else
    begin
      PortChoisi := 0;
      PortLibre := True;
      MontrerPastille(COrange, 'Windows choisira le port automatiquement.');
    end;
  end
  else
  begin
    Port := StrToIntDef(Trim(EditPort.Text), 0);

    if (Port < 1) or (Port > 65535) then
    begin
      PortChoisi := 0;
      PortLibre := False;
      MontrerPastille(CRouge, 'Indiquez un port entre 1 et 65535.');
    end
    else
    begin
      MontrerPastille(CGris, 'Vérification du port ' + IntToStr(Port) + '…');

      Rapport := ExpandConstant('{tmp}\port.ini');
      DeleteFile(Rapport);
      Exec(OutilChemin, 'probe-port --port ' + IntToStr(Port) + ' --format ini --encodage utf8 --sortie ' + AddQuotes(Rapport),
        '', SW_HIDE, ewWaitUntilTerminated, Code);
      ChargerIni(Rapport, RapportIni);

      Etat := IniGet(RapportIni, 'ports', '1_etat', '');
      Msg := IniGet(RapportIni, 'ports', '1_message', 'Résultat indisponible.');

      PortChoisi := Port;
      PortLibre := (Etat = 'libre');

      if PortLibre then MontrerPastille(CVert, Msg)
      else MontrerPastille(CRouge, Msg + ' Choisissez un autre port.');
    end;
  end;

  ActualiserAdresse;
  AppliquerBoutonSuivant;
end;

procedure SondeTimer(H: LongWord; Msg: LongWord; IdEvent: LongWord; Time: LongWord);
begin
  ActualiserSonde;
end;

procedure ProgrammerSonde;
begin
  ArreterSonde;
  PortLibre := False;
  PortChoisi := 0;
  MontrerPastille(CGris, 'Vérification en cours…');
  AppliquerBoutonSuivant;
  TimerId := SetTimer(0, 0, 450, TimerCallback);
end;

procedure PortRadioClick(Sender: TObject);
begin
  EditPort.Enabled := RadioManuel.Checked;
  if RadioManuel.Checked then ProgrammerSonde
  else ActualiserSonde;
end;

procedure PortEditChange(Sender: TObject);
begin
  if RadioManuel.Checked then ProgrammerSonde;
end;

procedure CarteChange(Sender: TObject);
begin
  ActualiserAdresse;
end;

procedure RemplirCartes;
var
  I, N, Nb, Choix: Integer;
  Nom, Ip, Suffixe, TypeC: String;
begin
  N := IniInt(DetectIni, 'reseau', 'interfaces_count', 0);
  Nb := 0;
  Choix := -1;

  for I := 1 to N do
  begin
    if IniGet(DetectIni, 'reseau', 'interfaces_' + IntToStr(I) + '_eligible', '0') = '1' then
    begin
      Nom := IniGet(DetectIni, 'reseau', 'interfaces_' + IntToStr(I) + '_nom', '');
      Ip := IniGet(DetectIni, 'reseau', 'interfaces_' + IntToStr(I) + '_adresses_1_ip', '');
      TypeC := IniGet(DetectIni, 'reseau', 'interfaces_' + IntToStr(I) + '_type', 'autre');

      if TypeC = 'ethernet' then Suffixe := '  (câble)'
      else if TypeC = 'wifi' then Suffixe := '  (Wi-Fi)'
      else Suffixe := '';

      SetArrayLength(IpCartes, Nb + 1);
      SetArrayLength(NomsCartes, Nb + 1);
      IpCartes[Nb] := Ip;
      NomsCartes[Nb] := Nom;
      ComboCarte.Items.Add(Nom + '  —  ' + Ip + Suffixe);

      if IniGet(DetectIni, 'reseau', 'interfaces_' + IntToStr(I) + '_recommandee', '0') = '1' then Choix := Nb;
      Nb := Nb + 1;
    end;
  end;

  SetArrayLength(IpCartes, Nb + 1);
  SetArrayLength(NomsCartes, Nb + 1);
  IpCartes[Nb] := IniGet(DetectIni, 'reseau', 'adresse_recommandee', '');
  NomsCartes[Nb] := '';
  ComboCarte.Items.Add('Toutes les cartes réseau');

  if Choix < 0 then Choix := Nb;
  ComboCarte.ItemIndex := Choix;
end;

procedure CreerPageReseau;
var
  L: Integer;
begin
  PageReseau := CreateCustomPage(PageAdmin.ID, 'Réseau du serveur',
    'Choisissez comment les postes joindront ce serveur.');
  L := PageReseau.SurfaceWidth;

  NouvelleEtiquette(PageReseau, 'Carte réseau du serveur', 0, 0, L, True);
  ComboCarte := TNewComboBox.Create(PageReseau);
  ComboCarte.Parent := PageReseau.Surface;
  ComboCarte.Left := 0; ComboCarte.Top := ScaleY(20); ComboCarte.Width := L;
  ComboCarte.Style := csDropDownList;
  RemplirCartes;
  ComboCarte.OnChange := @CarteChange;

  NouvelleEtiquette(PageReseau,
    'Le serveur reste joignable sur toutes ses cartes. Ce choix sert à afficher l''adresse réseau recommandée.',
    0, ScaleY(48), L, False).Font.Color := CGris;

  NouvelleEtiquette(PageReseau, 'Adresse à ouvrir dans le navigateur', 0, ScaleY(96), L, True);
  LblAdresse := NouvelleEtiquette(PageReseau, '', 0, ScaleY(114), L, False);
  LblAdresse.Font.Size := LblAdresse.Font.Size + 2;
  LblAdresse.Font.Color := CBleu;

  NouvelleEtiquette(PageReseau, 'Port d''écoute', 0, ScaleY(148), L, True);

  RadioAuto := TNewRadioButton.Create(PageReseau);
  RadioAuto.Parent := PageReseau.Surface;
  RadioAuto.Left := 0; RadioAuto.Top := ScaleY(168); RadioAuto.Width := L;
  RadioAuto.Caption := 'Automatique (recommandé)';
  RadioAuto.Checked := True;
  RadioAuto.OnClick := @PortRadioClick;

  RadioManuel := TNewRadioButton.Create(PageReseau);
  RadioManuel.Parent := PageReseau.Surface;
  RadioManuel.Left := 0; RadioManuel.Top := ScaleY(192); RadioManuel.Width := ScaleX(150);
  RadioManuel.Caption := 'Je choisis le port :';
  RadioManuel.OnClick := @PortRadioClick;

  EditPort := TEdit.Create(PageReseau);
  EditPort.Parent := PageReseau.Surface;
  EditPort.Left := ScaleX(156); EditPort.Top := ScaleY(190); EditPort.Width := ScaleX(70);
  EditPort.MaxLength := 5;
  if PortRecommande > 0 then EditPort.Text := IntToStr(PortRecommande) else EditPort.Text := '8080';
  EditPort.Enabled := False;
  EditPort.OnChange := @PortEditChange;

  LblPastille := NouvelleEtiquette(PageReseau, '●', 0, ScaleY(228), ScaleX(16), False);
  LblPastille.Font.Color := CGris;
  LblPastilleTexte := NouvelleEtiquette(PageReseau, '', ScaleX(18), ScaleY(228), L - ScaleX(18), False);
  LblPastilleTexte.Font.Color := CTexte;

  PortLibre := True;
  PortChoisi := PortRecommande;
end;

function ValiderSauvegarde: Boolean;
var
  Chemin, Test: String;
begin
  Result := False;
  Chemin := RemoveBackslashUnlessRoot(Trim(PageSauvegarde.Values[0]));

  if (Length(Chemin) < 4) or (Copy(Chemin, 2, 2) <> ':\') then
  begin
    MsgBox('Indiquez un dossier complet avec la lettre du disque (ex. E:\Sauvegardes).', mbError, MB_OK);
    Exit;
  end;

  if Pos(Lowercase(ExpandConstant('{autopf}')), Lowercase(Chemin)) = 1 then
  begin
    MsgBox('Ce dossier est réservé au programme : choisissez-en un autre.', mbError, MB_OK);
    Exit;
  end;

  if not ForceDirectories(Chemin) then
  begin
    MsgBox('Ce dossier ne peut pas être créé (disque absent ou protégé).', mbError, MB_OK);
    Exit;
  end;

  Test := AddBackslash(Chemin) + '.cm-test';
  if not SaveStringToFile(Test, 'ok', False) then
  begin
    MsgBox('Impossible d''écrire dans ce dossier. Vérifiez les droits.', mbError, MB_OK);
    Exit;
  end;
  DeleteFile(Test);

  if CompareText(ExtractFileDrive(Chemin), ExtractFileDrive(ExpandConstant('{sys}'))) = 0 then
  begin
    if MsgBox('Ce dossier est sur le même disque que Windows.' + CRLF + CRLF +
              'Un autre disque physique ou une clé USB est plus sûr en cas de panne de disque.' + CRLF + CRLF +
              'Garder tout de même ce dossier ?', mbConfirmation, MB_YESNO) <> IDYES then Exit;
  end;

  PageSauvegarde.Values[0] := Chemin;
  Result := True;
end;

procedure CreerPageSauvegarde;
var
  L: Integer;
  Cadre: TNewStaticText;
begin
  PageSauvegarde := CreateInputDirPage(PageReseau.ID, 'Sauvegardes et énergie',
    'Où conserver les sauvegardes de votre base ?',
    'Une sauvegarde est créée chaque nuit. Choisissez un dossier sur un AUTRE disque si possible.',
    False, '');
  PageSauvegarde.Add('Dossier des sauvegardes :');
  PageSauvegarde.Values[0] := ExpandConstant('{commonappdata}\CyberManager\sauvegardes');

  L := PageSauvegarde.SurfaceWidth;

  Cadre := NouvelleEtiquette(PageSauvegarde,
    'Conseil : en cas de coupure ou de panne de disque, cette copie protégera votre historique.',
    0, PageSauvegarde.Edits[0].Top + ScaleY(54), L, False);
  Cadre.Font.Color := CGris;

  CocheVeille := TNewCheckBox.Create(PageSauvegarde);
  CocheVeille.Parent := PageSauvegarde.Surface;
  CocheVeille.Left := 0;
  CocheVeille.Top := Cadre.Top + Cadre.Height + ScaleY(28);
  CocheVeille.Width := L;
  CocheVeille.Caption := 'Empêcher la mise en veille et l''hibernation de cet ordinateur (recommandé)';
  CocheVeille.Checked := True;
end;

function LibelleMode: String;
var
  Installee: String;
begin
  Installee := IniGet(DetectIni, 'installation', 'version_code', '');

  if Mode = 'mise_a_jour' then Result := 'Mise à jour de la version ' + Installee + ' vers la version {#AppVersion}'
  else if Mode = 'reparation' then Result := 'Réparation de la version {#AppVersion}'
  else if Mode = 'retrogradation' then Result := 'Installation impossible (version plus récente déjà installée)'
  else Result := 'Première installation de la version {#AppVersion}';
end;

procedure AdapterBienvenue;
var
  Installee: String;
begin
  Installee := IniGet(DetectIni, 'installation', 'version_code', '');

  if Mode = 'mise_a_jour' then
  begin
    WizardForm.WelcomeLabel1.Caption := 'Mise à jour de Cyber Manager';
    WizardForm.WelcomeLabel2.Caption :=
      'La version ' + Installee + ' est installée. Cet assistant la met à jour vers {#AppVersion}.' + CRLF + CRLF +
      'Vos données sont conservées intactes.' + CRLF + CRLF + 'Cliquez sur Suivant pour continuer.';
  end
  else if Mode = 'reparation' then
  begin
    WizardForm.WelcomeLabel1.Caption := 'Réparation de Cyber Manager';
    WizardForm.WelcomeLabel2.Caption :=
      'Cyber Manager {#AppVersion} est déjà installé. Cet assistant remet en état le service et le pare-feu.' + CRLF + CRLF +
      'Vos données ne sont pas modifiées.' + CRLF + CRLF + 'Cliquez sur Suivant pour continuer.';
  end
  else if Mode = 'retrogradation' then
  begin
    WizardForm.WelcomeLabel1.Caption := 'Version plus récente déjà installée';
    WizardForm.WelcomeLabel2.Caption :=
      'La version ' + Installee + ' est déjà présente. Le retour à une version antérieure est refusé pour protéger vos données.';
  end
  else
  begin
    WizardForm.WelcomeLabel1.Caption := 'Bienvenue dans l''installation de Cyber Manager';
    WizardForm.WelcomeLabel2.Caption :=
      'Cet assistant installe le serveur de votre cybercafé, version {#AppVersion}.' + CRLF + CRLF +
      'Tout est automatique (zéro terminal). Cliquez sur Suivant pour commencer.';
  end;
end;

procedure AdapterFin;
var
  Titre, Texte: String;
begin
  if InstallEchec then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'L''installation n''a pas pu être terminée';
    Texte := DernierMessage + CRLF + CRLF +
             'Vos données existantes n''ont pas été modifiées. Vous pouvez relancer l''installation.';
    if JournalChemin <> '' then Texte := Texte + CRLF + CRLF + 'Journal : ' + JournalChemin;
    WizardForm.FinishedLabel.Caption := Texte;
    Exit;
  end;

  if Mode = 'mise_a_jour' then Titre := 'Mise à jour terminée'
  else if Mode = 'reparation' then Titre := 'Réparation terminée'
  else Titre := 'Cyber Manager est installé';

  Texte := 'Le serveur démarre en arrière-plan et se lancera tout seul avec Windows.' + CRLF + CRLF +
           'Adresse du serveur : ' + AdresseServeur + CRLF;

  if CodeInstall <> '' then
    Texte := Texte + 'Code d''installation des postes : ' + CodeInstall + CRLF;

  Texte := Texte + CRLF + 'La télécommande (près de l''horloge) affiche l''état du serveur.';

  if AvertissementsFin <> '' then
    Texte := Texte + CRLF + CRLF + 'À noter :' + CRLF + AvertissementsFin;

  WizardForm.FinishedHeadingLabel.Caption := Titre;
  WizardForm.FinishedLabel.Caption := Texte;
end;

procedure AjouterEtape(var Liste: TArrayOfString; const Nom: String);
var
  N: Integer;
begin
  N := GetArrayLength(Liste);
  SetArrayLength(Liste, N + 1);
  Liste[N] := Nom;
end;

procedure ProvisionnerServeur;
var
  Etapes: TArrayOfString;
  I, Total: Integer;
  Ok, Reessayer: Boolean;
  Nom: String;
begin
  AjouterEtape(Etapes, 'dossiers');
  AjouterEtape(Etapes, 'configuration');
  AjouterEtape(Etapes, 'taches');
  AjouterEtape(Etapes, 'parefeu');
  AjouterEtape(Etapes, 'base');
  if EstNouvelle then AjouterEtape(Etapes, 'sauvegarde');
  AjouterEtape(Etapes, 'service');
  if EstNouvelle and ValVeille then AjouterEtape(Etapes, 'veille');
  AjouterEtape(Etapes, 'demarrer');

  Total := GetArrayLength(Etapes);
  WizardForm.ProgressGauge.Min := 0;
  WizardForm.ProgressGauge.Max := Total * 100;
  WizardForm.ProgressGauge.Position := 0;
  WizardForm.FilenameLabel.Caption := '';

  for I := 0 to Total - 1 do
  begin
    Nom := Etapes[I];
    WizardForm.StatusLabel.Caption := LibelleEtape(Nom);
    WizardForm.ProgressGauge.Position := I * 100 + 20;

    repeat
      Ok := ExecuterEtape(Nom, Nom = 'base');
      Reessayer := False;

      if not Ok then
      begin
        if not WizardSilent then
          Reessayer := (MsgBox(LibelleEtape(Nom) + CRLF + CRLF + DernierMessage + CRLF + CRLF +
                               'Voulez-vous réessayer ?', mbError, MB_RETRYCANCEL) = IDRETRY);
      end;
    until Ok or (not Reessayer);

    if not Ok then
    begin
      InstallEchec := True;
      Break;
    end;

    WizardForm.ProgressGauge.Position := (I + 1) * 100;
  end;

  if InstallEchec and WizardSilent then Abort;
end;

function InitializeSetup: Boolean;
var
  Code: Integer;
  Rapport: String;
begin
  Result := False;

  DetectIni := TStringList.Create;
  RapportIni := TStringList.Create;
  CodeInstall := '';
  AvertissementsFin := '';
  AvertCount := 0;
  InstallEchec := False;

  DotNetOk := DotNetPresent;
  if not DotNetOk then
  begin
    MsgBox('Le composant Microsoft .NET 8 (Desktop Runtime x64) ou plus récent est requis.' + CRLF + CRLF +
           'Installez-le puis relancez l''assistant.', mbError, MB_OK);
    Exit;
  end;

  VcOk := VcRedistPresent;

  ExtractTemporaryFile('CyberManagerSetupTool.exe');
  OutilChemin := ExpandConstant('{tmp}\CyberManagerSetupTool.exe');
  Rapport := ExpandConstant('{tmp}\detect.ini');
  DeleteFile(Rapport);

  if (not Exec(OutilChemin, 'detect --version-cible {#AppVersion} --format ini --encodage utf8 --sortie ' + AddQuotes(Rapport),
               '', SW_HIDE, ewWaitUntilTerminated, Code)) or (not FileExists(Rapport)) then
  begin
    MsgBox('L''analyse de l''ordinateur a échoué (code ' + IntToStr(Code) + ').' + CRLF + CRLF +
           'Vérifiez la présence du runtime .NET puis relancez.', mbError, MB_OK);
    Exit;
  end;

  ChargerIni(Rapport, DetectIni);

  Mode := IniGet(DetectIni, 'installation', 'mode', 'nouvelle');
  DonneesExistantes := (IniGet(DetectIni, 'installation', 'donnees_existantes', '0') = '1');
  ServiceExiste := (IniGet(DetectIni, 'installation', 'service_existe', '0') = '1');
  SessionsEnCours := IniInt(DetectIni, 'installation', 'sessions_en_cours', 0);
  PortRecommande := IniInt(DetectIni, 'meta', 'port_recommande', 0);

  EstNouvelle := (Mode = 'nouvelle') or (not DonneesExistantes);
  BloquantsGlobal := (IniInt(DetectIni, 'bloquants', 'count', 0) > 0) or (not VcOk);

  if IniGet(DetectIni, 'meta', 'port_origine', 'aucun') = 'repli' then
    MessagePortAuto := IniGet(DetectIni, 'ports', '1_message', '') + ' Le port ' + IntToStr(PortRecommande) + ' est proposé à la place.'
  else if PortRecommande > 0 then
    MessagePortAuto := 'Le port ' + IntToStr(PortRecommande) + ' est libre.'
  else
    MessagePortAuto := '';

  Result := True;
end;

procedure InitializeWizard;
begin
  TimerId := 0;
  TimerCallback := CreateCallback(@SondeTimer);

  CreerPageVerif;
  CreerPageAdmin;
  CreerPageReseau;
  CreerPageSauvegarde;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if PageID = PageAdmin.ID then Result := (not EstNouvelle) or DonneesExistantes
  else if PageID = PageReseau.ID then Result := not EstNouvelle
  else if PageID = PageSauvegarde.ID then Result := not EstNouvelle;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  ArreterSonde;

  if CurPageID = wpWelcome then
    AdapterBienvenue
  else if CurPageID = PageVerif.ID then
  begin
    RemplirVerifications;
    WizardForm.NextButton.Enabled := not BloquantsGlobal;
  end
  else if CurPageID = PageReseau.ID then
    ActualiserSonde
  else if CurPageID = wpFinished then
    AdapterFin;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = PageVerif.ID then Result := ValiderVerif
  else if CurPageID = PageAdmin.ID then Result := ValiderAdmin
  else if CurPageID = PageReseau.ID then
  begin
    if not PortLibre then
    begin
      MsgBox('Ce port n''est pas utilisable. Choisissez-en un autre ou revenez à Automatique.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = PageSauvegarde.ID then Result := ValiderSauvegarde;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  S: String;
begin
  S := LibelleMode + NewLine + NewLine;
  S := S + 'Programme :' + NewLine + Space + ExpandConstant('{app}') + NewLine + NewLine;
  S := S + 'Données :' + NewLine + Space + ExpandConstant('{commonappdata}\CyberManager') + NewLine;

  if EstNouvelle then
  begin
    S := S + NewLine + 'Cyber : ' + ValNomCyber + NewLine;
    if not DonneesExistantes then
      S := S + 'Administrateur : ' + ValAdminNom + ' (' + ValAdminEmail + ')' + NewLine;

    if PortChoisi > 0 then S := S + 'Port : ' + IntToStr(PortChoisi) + NewLine
    else S := S + 'Port : choisi automatiquement' + NewLine;

    S := S + 'Adresse : ' + AdresseServeur + NewLine;
    S := S + 'Sauvegardes : ' + ValSauvegarde + NewLine;

    if ValVeille then S := S + 'Mise en veille : désactivée' + NewLine
    else S := S + 'Mise en veille : inchangée' + NewLine;
  end
  else
    S := S + NewLine + 'Vos réglages actuels sont conservés.' + NewLine;

  Result := S;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  WizardForm.NextButton.Caption := 'Arrêt du serveur…';
  WizardForm.NextButton.Update;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM CyberManagerServer.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);

  if ServiceExiste then
  begin
    if not ExecuterEtape('arreter', False) then
      Result := 'Le serveur n''a pas pu être arrêté proprement : ' + DernierMessage;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then ProvisionnerServeur;
end;

procedure DeinitializeSetup;
begin
  ArreterSonde;
  if DetectIni <> nil then DetectIni.Free;
  if RapportIni <> nil then RapportIni.Free;
end;
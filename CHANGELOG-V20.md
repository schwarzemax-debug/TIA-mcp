# Changelog – TIA Portal V20 MCP-Server

## 2026-11-20 (Nachtrag 3: Unit-Tests, Build b8)

Unit-Tests ohne TIA-Verbindung ausgeführt: **275 von 275 bestanden** (vorher 24 Fehler). Nicht ausgeführt wurden die
Integrationstests `Test1Portal`, `Test2ProjectSession`, `Test21Project`, `Test3Devices`, `Test4Software` und
`Test5McpServer`: Sie sind für TIA V21 und Testprojekte unter `D:\Siemens\…` geschrieben und würden Projekte im
laufenden TIA öffnen/schließen. Ausgerollt: Build **„b8“** (Vorgänger in `server\_b7`), Claudes Verbindung nutzt ihn.
- **Bug:** Die Prompts `OpenProject`/`OpenTiaProject` wiesen das Modell an, `path` zu übergeben – das Tool heißt
  `projectpath` (bei `OpenProject` Pflicht → Fehler). Korrigiert.
- **Bug:** `WriteBlockSource` hatte als einziges Tool keinen Prompt → ergänzt.
- **Regression von mir behoben:** Seit dem Entfernen der `Siemens.Collaboration`-Pakete fand der Testprozess
  `Siemens.Engineering.dll` nicht mehr (11 Tests). Die Tests registrieren den Resolver jetzt selbst (`AssemblyHooks`).
  `Settings.TiaMajorVersion` ermittelt die installierte TIA-Version, statt fest V21 anzunehmen.
- Veraltete Testerwartungen angepasst: Parametername `projectpath` statt `path` (705, 1114, 1115, 1131, 1132),
  `projectpath` bei `TiaConnect` optional, Projektpfad nicht bindend (1151, prüft jetzt das sichere Verhalten),
  `project` → `project-path` bei `tia init` (1087), Tippfehler (1030). Test 1054 entfernt: Er widersprach Test 1052.
- Hinweis: TIA fragt bei **jedem neuen Build** einmal nach dem Zugriff. Wartet der Dialog, läuft Claudes erster
  Aufruf in eine Zeitüberschreitung; nach „Ja“ einfach wiederholen.

## 2026-11-20 (Nachtrag 2: Build b7, `--project-path`)

Ausgerollt: Build **„b7“** (Vorgänger in `server\_b6`). Claudes eigene MCP-Verbindung nutzt b7; die App startet den
Server nach dem Beenden selbst neu.
- `--project-path` ist kein „bindender“ Preset mehr. Er hat als solcher nie gegriffen (Tool-Parameter heißt
  `projectpath`, Preset `projectPath`), der Server-Hinweistext behauptete aber, der Projektpfad sei fest und kein
  Tool-Argument. Bewusst **nicht** „repariert“: bindend würde `TiaConnect` immer das konfigurierte Projekt öffnen und
  das offene Projekt vorher **ohne Speichern schließen**. Die Option bleibt, was sie im Code tatsächlich ist: Rückfall,
  wenn in TIA kein Projekt offen ist. Getestet mit `--project-path` = Projekt1: `TiaConnect` bleibt bei Projekt2.
- Testprojekt `TiaMcpServer.Test` übersetzt wieder: Verweis auf `Siemens.Engineering.dll` ergänzt (kam früher über die
  entfernten `Siemens.Collaboration`-Pakete). `Test8ToolDefaults` an den Preset-Wegfall angepasst. Tests **nicht
  ausgeführt** (laut AGENTS.md nur auf Anfrage).

## 2026-11-20 (Nachtrag, nach Abschalten von Smart App Control)

Ausgerollt: Build **„b6“** nach `%USERPROFILE%\TiaMcp\server` (Vorgänger in `server\_b3`, `_b4`, `_b5`; Original-Backup
`server.bak-20261120`). Damit sind auch alle früheren *(b4)*-Einträge aktiv. Gegen TIA getestet:
- `CopyBlock` einer SCL-FC (`FC_Skalierung` → `FC_Skalierung_Kopie`, FC2) übersetzt jetzt fehlerfrei.
- Compile-Report: die Hardware-Warnung steht unter Objekt „General warnings“.
- `PreviewImport` *(neu in b6)*: Dateien in Unterordnern werden der passenden Gruppe zugeordnet (vorher: alles in
  die Wurzel → falsche Konflikte bei `FB_Motor`/`FB_Stueckzaehler`). Der Systemordner (`Programmbausteine`) wird
  auch in `groupPath` ignoriert; der Gruppenvergleich ist exakt (vorher wurde `Test/A` wie `A` behandelt).
- MCP-Prompts `CopyBlock`/`CopyType`/`MoveBlock` *(neu in b6)*: Text an das neue Verhalten angepasst (`newName`, freie
  Nummer, Export → Löschen → Import mit Rücksetzen).
- Abschluss: Testkopie gelöscht, `CompilePlcSoftware` 0 Fehler / 1 erwartete Warnung.

## 2026-11-20

Getestet gegen TIA Portal V20, `Projekt2` (CPU 1516-3 PN/DP), über Test-Client und Sichtprüfung in TIA (Computer Use).
Ausgerollt war zunächst Build „b3“; Einträge mit *(b4)* waren erst nur im Quellcode (Build „b4“ wurde von Smart App
Control blockiert) – inzwischen mit b6 ausgerollt, siehe Nachtrag oben.

### Kritisch
- **Server startete nicht mehr** („Connection closed“): Smart App Control blockiert seit 20.11. 10:59 die unsignierte
  DLL `Siemens.Collaboration.Net.TiaPortal.Openness.Resolver.dll`. Die NuGet-Pakete `Siemens.Collaboration.Net.*`
  wurden entfernt; Assembly-Auflösung und Gruppenprüfung („Siemens TIA Openness“) sind jetzt eigener Code
  (`Siemens/Openness.cs`, `Program.cs`). Der Server fügt den Nutzer nicht mehr selbst zur Gruppe hinzu.
- Fehlende Openness-Gruppe schrieb Text auf stdout (MCP-Kanal) → jetzt stderr + Exit-Code 2.

### Fehler behoben
- `CreateInstanceDB`: erzeugte bei automatischer Nummer **DB0** (Compile-Fehler „invalid number 0“). Jetzt erste freie Nummer.
- `CreateFB`: ging in V20 nur mit ProDiag. Jetzt LAD, FBD, SCL, STL (leerer FB aus SimaticML bzw. Quelle), erste freie
  FB-Nummer, kein Transaktions-Abbruch mehr („Commit … not allowed after an exception“).
- `CreateFB`/`CreateInstanceDB`: fehlende Gruppen werden angelegt (wie bei `WriteBlockSource`); doppelte Namen werden vorab gemeldet.
- `CopyBlock`/`CopyType`: scheiterten immer (Name CPU-weit eindeutig). Neuer Parameter `newName`, sonst automatisch
  `<Name>_1` …; Kopie bekommt eine freie Bausteinnummer; Kopieren in dieselbe Gruppe möglich.
- *(b4)* `CopyBlock` einer SCL-FC: Rückgabewert-Zuweisung über den Funktionsnamen wird beim Umbenennen mitgezogen.
- `MoveBlock`/`MoveType`: importierten vor dem Löschen → Namenskollision. Jetzt Export → Löschen → Import mit Rücksetzen;
  inkonsistente Quellen werden vorher übersetzt.
- `WhereUsed`: lieferte auch „verwendet selbst“ und Schnittstellen-Member. Jetzt nur echte Verwender (`UsedBy`,
  Instanz-DBs) mit `access` (Call/Read/Write/InstanceDB) und `locations` (z. B. „Main ▶ NW2 (Antrieb Band 1)“).
- `FindInCode`: inkonsistente Bausteine wurden stillschweigend übersprungen (0 Treffer trotz Verwendung) → jetzt in
  `unsearchable`; UDT-Suche in V20 (V21-API) → über `.udt`-Quelle; Treffer in KOP/FUP mit `network` („NW5“).
- `GetBlockInterface`: nur DBs → jetzt auch FB/FC/OB (aus SimaticML, inkl. Abschnitt, Startwert, Kommentar).
- `GetProjectTree`: jedes Hardware-Element doppelt → Duplikate entfernt.
- `Doctor`: „none found (>= V21)“ → erkennt jetzt V20.
- `PreviewImport`: `kind` akzeptiert Plural; *(b4)* rekursive Suche inkl. `.scl/.awl/.db/.udt`, Klassifizierung per Inhalt.
- `GetSourceBlock`: Fehlermeldung nannte nicht existierende Tools → verweist auf `GetXmlBlock`.
- Compile-Fehler (Exception) wurden zu einem grundlosen „compiling failed“ → TIA-Ursache wird weitergegeben.

### Verbesserungen
- `WriteBlockSource` (FUP/KOP-XML): offene Pins von Aufrufen und EN-Boxen (MOVE, ADD, CONVERT …) in FBD-Netzwerken werden
  automatisch als `<OpenCon>` ergänzt (TIA verweigert sonst den Import); Rückmeldung in `notes`.
- `WriteBlockSource`: UDTs werden jetzt ebenfalls übersetzt; Format `xml` wird korrekt gemeldet; Tool-Beschreibung mit
  vollständigem FUP-Rezept (Namespaces V20, Parts, Calls, Konstanten).
- Compile-Report: nur Zeilen mit Text; Spalte „Object“ nennt Baustein + Zeile/Netzwerk (z. B. „FC_Fehlertest (FC4) › 2“);
  *(b4)* Meldungen unter „General warnings“ bekommen den Gruppennamen.
- Import-Fehlermeldungen: Hinweis zur Lösung bei „… is not connected to the object with the UID …“.
- `CreateTag`/`UpdateTag`: neuer Parameter `comment` (Editiersprache des Projekts).
- Server-Hinweistext: widersprüchliche Anleitung („Connect, dann OpenProject“) korrigiert, Openness-Dialog erwähnt.

### Werkzeuge / Doku
- `mcp-session.ps1` + `mcp-call.ps1`: persistente Testsitzung (ein TIA-Zugriffsdialog statt einer pro Aufruf).
- `gen_fbd_main.ps1`: Generator für die FUP-Main (7 Netzwerke) als Vorlage.
- `ANLEITUNG.md` überarbeitet (FUP-Rezept, Smart App Control, korrigierte Tool-Liste).

### Im Projekt angelegt (nur im Speicher, **nicht gespeichert**)
- Variablentabelle `IO_Foerderband` (16 Tags mit Adressen und teils Kommentaren, Konstante `C_MaxStueck` = 100)
- `UDT_MotorStatus`, `FB_Motor` (SCL, FB1), `FB_Stueckzaehler` (SCL, FB2), `FC_Skalierung` (SCL, FC1),
  `DB_Motor_Band1` (DB2), `DB_Zaehler_Band1` (DB1), Beobachtungstabelle `WT_Foerderband`
- `Main` (OB1) **in FUP**, 7 Netzwerke, übersetzt mit 0 Fehlern (1 Warnung: E/A-Adressen ohne konfigurierte Peripherie)

### Offen
- ~~Smart App Control blockiert neue Builds~~ → vom Nutzer abgeschaltet, erledigt.
- TIA meldet im Zugriffsdialog „2 TIA Portal-Instanz(en) aktiv“ – vermutlich läuft noch eine zweite (unsichtbare) Instanz.
- ~~Desktop-App-Config mit veraltetem `--project-path`~~ → durch `--software-path PLC_1` ersetzt (Sicherung `.bak-20261120`);
  greift beim nächsten Start der Claude-App.
- ~~Zwei alte Server-Prozesse vom 18.11.~~ → beendet; die App hat den Server automatisch mit b6 neu gestartet.
- ~~„`--project-path` wirkt nie“~~ – diese Diagnose war falsch, korrigiert in b7 (siehe Nachtrag 2).

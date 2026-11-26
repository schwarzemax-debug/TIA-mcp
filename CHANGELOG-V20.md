# Changelog – TIA Portal V20 MCP-Server

## 2026-11-26 – v0.4.1 (Push mit FUP, keine Rückfragen)

Anlass: Ein Agent (GitHub Copilot CLI) scheiterte beim Aufbau eines FUP-`Main` an `TiaPush`: jeder Versuch endete mit
„Error when calling method 'CommitOnDispose' … Commit of a Transaction is not allowed after an exception“, ohne
Ursache. Der Nutzer wurde außerdem regelmäßig gefragt, ob überschrieben werden darf. Reproduziert gegen TIA V20
(`Projekt1`), behoben, getestet (Unit-Tests 280/280, Integrationstest gegen TIA, siehe unten).

### Fehler behoben
- **`TiaPush` / `ImportPlcProgramChanges` / `OverwritePlcProgram`:** Alle Dateien liefen in *einer* TIA-Transaktion.
  Scheiterte eine Datei, war die Transaktion vergiftet: Der Commit am Ende schlug fehl, **alle** Dateien wurden
  verworfen, und die eigentliche TIA-Meldung ging verloren (nur „CommitOnDispose“). Die Runden-Wiederholung konnte
  so nie greifen. Jetzt: exklusiver Zugriff für den ganzen Aufruf, **eine Transaktion pro Datei**. Eine fehlerhafte
  Datei wird allein zurückgerollt, `Failures` enthält die Meldung von TIA (z. B. „'IdentCon' does not exist at the
  object with UID '99'“ mit Zeilennummer), die übrigen Dateien bleiben importiert, die nächste Runde startet sauber.
- **FUP-XML über `TiaPush`:** Offene Pins von Aufrufen und EN-Boxen (`en`, ungenutzte Ausgänge) wurden nur bei
  `WriteBlockSource` ergänzt, beim Push nicht → TIA lehnte die Datei ab. Jetzt auch beim Push (aus einer Kopie; die
  Datei im Export-Ordner bleibt unverändert), neues Antwortfeld `Notes`.
- **Commit-Fehler allgemein:** „Commit … not allowed after an exception“ wird zu einer Meldung, die sagt, dass alles
  zurückgerollt wurde, plus die TIA-Ursache.
- **`TiaPull` in einen neuen Ordner verlor alle SCL-Bausteine:** Das automatisch angelegte `tia.json` enthielt die
  V21-Vorgaben (SIMATIC SD); V20 exportiert SCL-Bausteine so nicht („mixed programming languages“). Für V20 jetzt
  XML für KOP/FUP, `.scl`, `.awl`, `.db`, `.udt`.
- **Inkonsistente Bausteine:** `GetXmlBlock`, `GetSourceBlock`, `GetBlockInterface`, `ExportBlock`, `TiaPull`/
  `ExportPlcProgram` (und die Typ-Varianten) übersetzen den Baustein jetzt einmal selbst, statt mit „inconsistent,
  compile first“ abzubrechen bzw. ihn stillschweigend zu überspringen (typisch direkt nach einem Push).
- **„busy with another request for more than 30 s“:** 30 s waren kürzer als ein normaler Push/Übersetzen. Jetzt 120 s,
  und die Meldung nennt das noch laufende Tool und seine Laufzeit statt pauschal „vielleicht ein Dialog“.
- **`TiaConnect`:** „Failed to connect“ ohne Grund → enthält jetzt die TIA-Meldung und den Hinweis auf den
  Dialog „Openness Zugriff“ (kommt bei jedem neuen Server-Programm einmal).

### Geändert: keine Rückfragen mehr
- `TiaPush`, `ImportPlcProgramChanges`, `OverwritePlcProgram`, `ImportHardware`/`TiaPushHardware` und
  `WriteBlockSource` zeigen **keinen Bestätigungsdialog** mehr (MCP-Elicitation „Yes, import“) und lehnen Clients ohne
  Dialog nicht mehr ab. Änderungen bleiben im Speicher, jede Datei ist ein Rückgängig-Schritt in TIA.
- Tool-Beschreibungen und Prompts wiesen das Modell an, vor dem Import im Chat nachzufragen („only after the user
  agrees“) und nach jedem Schritt zu fragen, ob gespeichert werden soll. Entfernt; „nicht selbst speichern“ bleibt.
- Neuer Schalter **`--confirm-writes`** für Betreiber, die den Dialog wollen (altes Verhalten).
  `--trust-writes` wird noch akzeptiert, hat aber keine Wirkung mehr.
- README: Abschnitt „Freigaben im Client“ (Claude Code, Copilot CLI, VS Code), falls der *Client* nachfragt.

### Tests
- Unit-Tests: **280/280** bestanden (7 an das neue Verhalten angepasst, 5 neu: `--confirm-writes`/`--trust-writes`,
  Import ohne Dialog, V20-/V21-Vorgaben). Integrationstests (`Test1Portal` …) wie bisher nicht ausgeführt (V21, öffnen/
  schließen Projekte).
- Gegen TIA V20 (`Projekt1`): Fehler mit v0.4.0 reproduziert (FUP-OB mit FB-Aufruf ohne `en` → „CommitOnDispose“).
  Mit v0.4.1: derselbe OB importiert; Push aus gutem + kaputtem OB → guter importiert, kaputter mit TIA-Ursache in
  `Failures`, Runde 2 ohne Folgefehler; kompletter Ablauf `git init` → `TiaPull` → FUP-`Main.xml` (2 Netzwerke:
  `FB_Saege`, `FB_Laufband`) → `TiaPushPreview` → `TiaPush` → `CompilePlcSoftware` 0 Fehler; Client mit
  Dialog-Unterstützung bekam 0 Rückfragen; `WriteBlockSource` SCL/FUP, `FindInCode`, `WhereUsed`, `GetXmlBlock`.

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

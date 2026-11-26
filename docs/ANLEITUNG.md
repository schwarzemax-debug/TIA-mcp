# TIA Portal V20 MCP-Server – Anleitung für Agenten

Stand: 2026-11-26 (v0.4.1), getestet gegen TIA Portal V20, Projekte `Projekt1`/`Projekt2` (S7-1500, `PLC_1`).
Änderungen dieses Stands: siehe `CHANGELOG.md`.

## 1. Was das ist

Ein MCP-Server (stdio) für TIA Openness, aus dem Upstream-Projekt `heilingbrunner/tiaportal-mcp` (v0.4.0)
**für V20 neu gebaut** (V20 hat nur `Siemens.Engineering.dll`, die ausgelieferte Extension läuft nur mit V21).

Pfade in dieser Anleitung sind Beispiele aus der Installation in der [README](../README.md):

| Was | Wo |
| --- | --- |
| Server-EXE | `%USERPROFILE%\TiaMcp\server\TiaMcpServer.exe` |
| Claude-Code-Config | `%USERPROFILE%\.claude.json` bzw. `.mcp.json` (Server-Name `vscode-tiaportal-mcp`) |
| Claude-Desktop-Config | `%APPDATA%\Claude\claude_desktop_config.json` |
| Export-/Import-Ordner | `%USERPROFILE%\TiaMcp\Project` (enthält `tia.json`) |
| Quellcode | `src/TiaMcpServer` in diesem Repository |
| Test-Clients ohne Claude | `tools/mcp-client.ps1` (ein Aufruf), `tools/mcp-session.ps1` + `tools/mcp-call.ps1` (eine Sitzung, viele Aufrufe) |
| FUP-Beispiel | `tools/gen_fbd_main.ps1` erzeugt die FUP-`Main` des Förderband-Beispiels als SimaticML |
| TIA-Projekt | **immer das zum Aufrufzeitpunkt in TIA Portal geöffnete Projekt** |
| Softwarepfad (Preset) | `PLC_1` |

Voraussetzungen: TIA V20 läuft mit offenem Projekt, Windows-Nutzer in Gruppe „Siemens TIA Openness“, .NET Framework 4.8.
Die NuGet-Pakete `Siemens.Collaboration.Net.*` werden **nicht mehr** verwendet (siehe 5., Smart App Control).

## 2. Standard-Ablauf

1. `TiaConnect` **ohne `projectpath`** – hängt sich an die laufende TIA-Instanz und nimmt das dort geöffnete Projekt.
   Bei mehreren TIA-Instanzen `GetPortals` aufrufen und `portalId` angeben.
2. **TIA zeigt beim ersten Zugriff jedes neuen Server-Prozesses den Dialog „Openness Zugriff“.** Der Aufruf blockiert, bis
   der Nutzer in TIA „Ja“ klickt. Der Dialog liegt oft **hinter** dem TIA-Hauptfenster. Agent: Nutzer darauf hinweisen
   (oder – nur wenn der Nutzer das ausdrücklich will – per Computer Use „Ja“ klicken; „Ja, alle“ speichert die Freigabe dauerhaft, nur auf Nutzerwunsch).
3. Lesen/Schreiben mit den Tools unten. `softwarePath` und `exportPath` sind voreingestellt, **weglassen**.
4. Änderungen liegen nur **im Speicher**. `SaveProject` nur, wenn der Nutzer es ausdrücklich sagt.
5. `Disconnect` am Ende (schließt das Projekt nicht).

Der Server ist zustandsbehaftet: `Connect` und die Folgeaufrufe müssen in **derselben Server-Sitzung** laufen.

## 3. Lesen (getestet)

| Ziel | Tool |
| --- | --- |
| Struktur | `GetProjectTree`, `GetSoftwareTree`, `GetProject`, `GetDevices`, `GetPlcSummary` |
| Bausteine | `GetBlocks` (regexName), `GetBlocksWithHierarchy`, `GetBlockInfo` |
| Schnittstelle | `GetBlockInterface` – **jetzt für DB, FB, FC und OB** (FB/FC/OB aus dem SimaticML-Export, Baustein muss konsistent sein) |
| Quelltext | `GetSourceBlock` (SCL/AWL/DB), `GetXmlBlock` (alle Sprachen, v. a. KOP/FUP), `GetDocumentsBlock` (V20: nur SCL) |
| Tags / Konstanten | `GetTagTables`, `GetTags`, `GetTagInfo` (inkl. Kommentar), `GetConstants` |
| Typen | `GetTypes`, `GetTypeInfo`, `GetXmlType`, `GetSourceType` |
| Suche | `FindInCode` (Treffer in FUP/KOP mit Netzwerk `NW2`; inkonsistente Objekte werden unter `unsearchable` genannt), `WhereUsed` (nur echte Verwender, mit Zugriff und Ort „Main ▶ NW2 (Titel)“), `GetCrossReferences` |
| Kompilieren | `CompilePlcSoftware`, `CompileCodeBlock` (Report: nur Zeilen mit Text, Objekt = „FB_X (FB1) › Zeile“) |
| Export | `ExportPlcProgram`, `ExportBlock(s)`, `ExportTagTable(s)`, `TiaPull*` |

## 4. Schreiben (getestet am 2026-11-20)

- Tags: `CreateTagTable`, `CreateTag` (**neu: `comment`**), `UpdateTag` (**neu: `comment`**), `DeleteTag`, `CreateUserConstant`.
- Bausteine:
  - `WriteBlockSource` – **der Hauptweg**: SCL/AWL/DB/UDT als Quelltext oder KOP/FUP als SimaticML (`format: "xml"`).
    Erzeugt/ersetzt, kompiliert (jetzt auch UDTs) und liefert Compiler-Meldungen. Fehlende Gruppen werden angelegt.
  - `CreateFB` – **jetzt in LAD, FBD, SCL, STL** (leerer FB, erste freie FB-Nummer, fehlende Gruppe wird angelegt).
  - `CreateInstanceDB` – vergibt jetzt die erste freie DB-Nummer (vorher entstand `DB0`).
  - `CopyBlock`/`CopyType` – Kopie bekommt `newName` oder automatisch `<Name>_1`, `_2` … und eine freie Nummer
    (Namen sind CPU-weit eindeutig; vorher scheiterte jede Kopie). **Achtung im ausgerollten Build:** eine kopierte
    SCL-**FC** kompiliert nicht (`#FC_Alt := …` zeigt noch auf den alten Namen) – behoben im Quellcode, noch nicht ausgerollt (siehe 5.).
  - `MoveBlock`/`MoveType` – funktionieren jetzt (Export → Löschen → Import, bei Fehler Rücksetzen); inkonsistente
    Quellen werden vorher automatisch übersetzt.
  - `RenameBlock`, `DeleteBlock`, `CreateBlockGroup`, `DeleteBlockGroup`.
- **Keine Rückfragen** (seit v0.4.1): `WriteBlockSource`, `TiaPush`, `ImportPlcProgramChanges`, `OverwritePlcProgram`
  und der Hardware-Import laufen ohne Bestätigungsdialog. Agent: **nicht im Chat nachfragen**, ob etwas überschrieben
  werden darf, und nicht nach jedem Schritt fragen, ob gespeichert werden soll – nur nicht selbst speichern.
  Nur wenn der Betreiber den Server mit `--confirm-writes` startet, zeigt der Client einen Dialog („Yes, import“);
  den beantwortet der Nutzer. `--trust-writes` ist veraltet und ohne Wirkung.
- Fragt der Client selbst bei jedem Tool-Aufruf nach (Copilot CLI, VS Code, Claude Code), ist das eine Einstellung des
  Clients, siehe README „Freigaben im Client“.
- Alles läuft in einer TIA-Transaktion: schlägt ein Schritt fehl, wird er komplett zurückgerollt.
- Importe mehrerer Dateien (`TiaPush` …) haben **eine Transaktion pro Datei**: eine fehlerhafte Datei wird allein
  zurückgerollt, `Failures` nennt die Ursache von TIA (z. B. „'IdentCon' does not exist at the object with UID '99'“),
  die übrigen Dateien bleiben importiert. Fehlerhafte Datei korrigieren und erneut pushen.
- FUP-XML über `TiaPush`/`OverwritePlcProgram`: offene Pins werden wie bei `WriteBlockSource` ergänzt (`Notes`);
  die Datei im Export-Ordner bleibt unverändert.

### 4a. FUP (FBD) per SimaticML – Rezept

Getestet: OB1 `Main` mit 7 FUP-Netzwerken (UND, negierter Eingang, Zuweisung, FB-Aufruf mit Instanz-DB, FC mit
Rückgabewert, Vergleicher `>=`, SR-Flipflop, MOVE). Vorlage: `gen_fbd_main.ps1`.

- Kopf: `<Engineering version="V20" />`, Schnittstelle `.../SW/Interface/v5`, Netzwerk `.../SW/NetworkSource/FlgNet/v4`.
- `AttributeList` des Bausteins braucht `<Name>`, `<Namespace />`, `<Number>`, `<ProgrammingLanguage>FBD</ProgrammingLanguage>`.
- Pro Netzwerk ein `SW.Blocks.CompileUnit` mit `<ProgrammingLanguage>FBD</ProgrammingLanguage>`; `ID` eindeutig im Dokument
  (hex), `UId` eindeutig im Netzwerk. Kein `<Powerrail>` in FUP.
- Operanden: `<Access Scope="GlobalVariable"><Symbol><Component Name="Tag"/></Symbol></Access>`,
  Konstanten `LiteralConstant` (mit `ConstantType`) oder `GlobalConstant` (`<Constant Name="C_X"/>`).
- Logik: `Part Name="A"|"O"|"X"` (+ `TemplateValue Card`), `"Coil"` (Zuweisung), `"Sr"`, `"Ge"` (+ `TemplateValue SrcType`),
  `"Move"`; negierter Eingang `<Negated Name="in2"/>`.
- Aufrufe: `<Call><CallInfo Name="FB_X" BlockType="FB"><Instance Scope="GlobalVariable"><Component Name="DB_X"/></Instance>`
  + ein `<Parameter>` je Schnittstellen-Member. FC mit Rückgabewert: `Parameter Name="Ret_Val" Section="Return"`.
- **Offene Pins:** TIA verlangt, dass jeder Pin eines Aufrufs/einer EN-Box verdrahtet ist (auch `en` und ungenutzte
  Ausgänge). `WriteBlockSource` ergänzt diese jetzt automatisch als `<OpenCon>` und meldet das in `notes`.
- Instanz-DBs, Tags und Konstanten müssen vor dem Import existieren.

## 5. V20-/Rechner-Besonderheiten (wichtig)

- **Smart App Control (Windows)** war am 2026-11-20 von 10:59 bis ca. 14:25 erzwingend aktiv und hat unsignierte
  EXE/DLLs per Cloud-Reputation blockiert; der Nutzer hat es danach ausgeschaltet (Registry
  `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` = 0). Folgen der Episode:
  - Die unsignierten NuGet-DLLs `Siemens.Collaboration.Net.*` wurden blockiert → der Server startete gar nicht
    („Connection closed“). Behoben: Abhängigkeit entfernt, eigener Resolver/Gruppencheck.
  - Wird SAC je wieder aktiv, kann jeder neue eigene Build blockiert werden (Ereignisanzeige → CodeIntegrity,
    ID 3077/3118). Abhilfe ist eine Entscheidung des Nutzers (signieren oder SAC aus). Der Agent ändert
    Sicherheitseinstellungen nicht.
- `RenameWatchTable` geht in V20 nicht (`PlcWatchTable.Name` schreibgeschützt).
- `tia.json` im Export-Ordner steuert die Formate (XML/Quellformate, **nicht** SIMATIC SD).
- Inkonsistente Bausteine lassen sich nicht exportieren → erst `CompilePlcSoftware`.
- `ImportPlcProgramChanges`/`TiaPush` brauchen git im PATH und einen Export-Ordner unter git (`git init`, `TiaPull`,
  Commit als Basis); ohne git `OverwritePlcProgram` oder `WriteBlockSource` verwenden.
- `PreviewImport` akzeptiert `block(s)`/`type(s)` und durchsucht den Exportbaum rekursiv (auch `.scl/.awl/.db/.udt`).
  Unterordner zählen als Zielgruppe (wie beim echten Import), der Systemordner (`Programmbausteine`) wird ignoriert.
- `Doctor` erkennt jetzt V20.
- `CheckIsUpToDate`, `GetMarkedObjects`, `CommitChanges` gibt es nur in Multiuser-Sitzungen (`.als`).

## 6. Typische Fehler

| Symptom | Ursache / Lösung |
| --- | --- |
| MCP „Connection closed“ beim Start | Smart App Control blockiert die EXE/DLL (Ereignisanzeige CodeIntegrity 3077). Siehe 5. |
| `TiaConnect` hängt / „Failed to connect“ | Dialog „Openness Zugriff“ wartet in TIA (oft hinter dem Hauptfenster), einmal je neuem Server-Programm. Nutzer bestätigen lassen, dann `TiaConnect` wiederholen. |
| `The connection with the name 'en' is not connected …` | Pin ohne Draht im FUP-XML. `WriteBlockSource` ergänzt Calls/EN-Boxen automatisch; sonst `<OpenCon>` setzen. |
| `Missing 'Namespace' identifier attribute` | `<Namespace />` in der AttributeList des Bausteins fehlt. |
| `Commit of a Transaction is not allowed after an exception` | Bis v0.4.0 bei `TiaPush`: eine fehlerhafte Datei verdarb den ganzen Push und versteckte die Ursache. Ab v0.4.1 behoben (Transaktion pro Datei, Ursache in `Failures`). Sonst: „TIA Portal refused to commit …“ – alles zurückgerollt, Ursache steht dahinter. |
| `… busy with another request …` | Ein voriger Aufruf (Push, Übersetzen) läuft noch. Ab v0.4.1 wartet der nächste Aufruf bis 120 s und nennt das laufende Tool. |
| `Block '…' is inconsistent` beim Lesen/Exportieren | Ab v0.4.1 übersetzen `GetXmlBlock`, `TiaPull` usw. den Baustein vorher selbst; die Meldung kommt nur noch, wenn er nicht übersetzbar ist (`CompileCodeBlock` zeigt warum). |
| `TiaPull` in neuen Ordner liefert keine SCL-Bausteine | Bis v0.4.0 schrieb ein neues `tia.json` SIMATIC-SD-Formate, die V20 für SCL nicht kann. Ab v0.4.1 XML/Quelltext für V20; altes `tia.json` ggf. anpassen. |
| `Block not found` | Voller Pfad nötig (`Gruppe/Untergruppe/Name`), `GetBlocks`/`ResolveObjectPath` zeigt ihn. |
| Falsches Projekt geöffnet | `--project-path` ist nur Rückfall, wenn in TIA kein Projekt offen ist. `TiaConnect` ohne `projectpath` aufrufen. |

## 7. Neu bauen und ausrollen

```powershell
cd <Repo>\src\TiaMcpServer
dotnet build -c Release -o <Repo>\artifacts\neu
```

Vor dem Ausrollen mit `--doctor` starten: Wird der Start blockiert (Smart App Control), nicht ausrollen.
Ausrollen nach `%USERPROFILE%\TiaMcp\server`: laufende Server-Prozesse halten die EXE – gesperrte Dateien vorher
umbenennen (Windows erlaubt das) oder `Kill-TiaMcpServers.bat` ausführen, dann kopieren. Danach in Claude `/mcp` →
`vscode-tiaportal-mcp` neu verbinden.

## 8. Schneller Funktionstest ohne Claude

```powershell
# eine Sitzung (ein TIA-Dialog), beliebig viele Aufrufe; die Config braucht einen Eintrag "vscode-tiaportal-mcp"
cd <Repo>\tools
Start-Job { & "$using:PWD\mcp-session.ps1" -ConfigPath "$using:PWD\..\samples\claude-code\.mcp.json" -QueueDir C:\Temp\mcpq }
.\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Tool TiaConnect      # in TIA „Ja“ klicken
.\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Tool GetSoftwareTree
.\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Stop
```

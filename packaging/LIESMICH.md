# TIA-mcp – vorgebautes Paket für TIA Portal V20

Fertig gebauter MCP-Server, kein .NET SDK und kein Bauen nötig.
Quellcode und ausführliche Doku: https://github.com/schwarzemax-debug/TIA-mcp

## Voraussetzungen

- Windows 10/11 x64 mit .NET Framework 4.8 (ist vorhanden)
- **TIA Portal V20** installiert (bringt `Siemens.Engineering.dll` mit, die **nicht** im Paket ist)
- Benutzer in der lokalen Gruppe **„Siemens TIA Openness“** (danach ab- und anmelden)

## Installation

```powershell
# ZIP entpacken, im entpackten Ordner:
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -RegisterClaudeCode
```

Kopiert den Server nach `%USERPROFILE%\TiaMcp\server`, legt `%USERPROFILE%\TiaMcp\Project\tia.json` an, prüft mit
`--doctor` und meldet den Server in Claude Code an. Optionen: `-InstallDir`, `-SoftwarePath PLC_1`, `-ReadOnly`.
Für Claude Desktop oder VS Code die Vorlagen in `samples\` nehmen und `<NAME>` ersetzen.

Manuell geht es auch: Ordner `server\` irgendwohin kopieren und im MCP-Client eintragen:

```text
TiaMcpServer.exe --tia-major-version 20 --software-path PLC_1 --export-path <Exportordner> --keep-structure --allow-write
```

## Dateien

| Datei | Zweck |
| --- | --- |
| `Install.ps1` | Installationsskript (kopieren, entsperren, prüfen, in Claude Code anmelden) |
| `LIESMICH.md` | diese Anleitung |
| `LICENSE.txt` | MIT-Lizenz |
| `Kill-TiaMcpServers.bat` | beendet laufende Server-Prozesse (vor einem Update) |
| `Project\tia.json` | Exportformate für den Export-/Import-Ordner |
| `samples\claude-code\.mcp.json` | Config-Vorlage Claude Code |
| `samples\claude\claude_desktop_config.json` | Config-Vorlage Claude Desktop |
| `samples\vscode\mcp.json` | Config-Vorlage VS Code / Copilot |
| **`server\` – zur Ausführung nötig** | |
| `TiaMcpServer.exe` | der MCP-Server (stdio, x64, .NET Framework 4.8) |
| `TiaMcpServer.exe.config` | Laufzeit-Config mit Binding-Redirects (**nicht löschen**) |
| `TiaMcpServer.pdb` | Debug-Symbole (optional, für lesbare Fehlermeldungen) |
| `ModelContextProtocol.dll`, `ModelContextProtocol.Core.dll` | MCP-SDK |
| `Microsoft.Extensions.*.dll` (29 Dateien) | Hosting, Logging, Konfiguration, DI, AI-Abstraktionen |
| `Microsoft.Bcl.AsyncInterfaces.dll`, `Microsoft.Bcl.Memory.dll` | .NET-Rückportierungen für 4.8 |
| `System.*.dll` (12 Dateien) | Json, Memory, Pipelines, Channels, Buffers u. a. (Rückportierungen für 4.8) |
| *nicht enthalten:* `Siemens.Engineering.dll` | kommt aus der TIA-Installation (`Portal V20\PublicAPI\V20`) |

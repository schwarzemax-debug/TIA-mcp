<#
.SYNOPSIS
  Installiert den vorgebauten TIA-MCP-Server aus dem Release-Paket (ohne .NET SDK, ohne Bauen).

.EXAMPLE
  # aus dem entpackten Ordner, PowerShell:
  powershell -ExecutionPolicy Bypass -File .\Install.ps1 -RegisterClaudeCode
#>
param(
  [string]$InstallDir = (Join-Path $env:USERPROFILE 'TiaMcp'),
  [string]$SoftwarePath = 'PLC_1',
  [switch]$ReadOnly,
  [switch]$RegisterClaudeCode,
  [string]$ServerName = 'vscode-tiaportal-mcp'
)
$ErrorActionPreference = 'Stop'
$tiaVersion = 20
$serverDir = Join-Path $InstallDir 'server'
$exportDir = Join-Path $InstallDir 'Project'
$exe = Join-Path $serverDir 'TiaMcpServer.exe'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

Step 'Voraussetzungen pruefen'
$tiaDll = "C:\Program Files\Siemens\Automation\Portal V$tiaVersion\PublicAPI\V$tiaVersion\Siemens.Engineering.dll"
if (-not (Test-Path $tiaDll)) { throw "TIA Portal V$tiaVersion mit Openness nicht gefunden: $tiaDll" }
$inGroup = ([Security.Principal.WindowsIdentity]::GetCurrent().Groups | ForEach-Object {
    try { $_.Translate([Security.Principal.NTAccount]).Value } catch { } }) -match '\\Siemens TIA Openness$'
if (-not $inGroup) {
  Write-Warning "Benutzer nicht in der Gruppe 'Siemens TIA Openness'. Ein Administrator muss ihn hinzufuegen, danach ab- und wieder anmelden."
}

Step "Dateien nach $InstallDir kopieren"
$running = Get-Process TiaMcpServer -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) { throw "TiaMcpServer.exe laeuft noch aus $serverDir. Claude schliessen oder Kill-TiaMcpServers.bat ausfuehren." }
New-Item -ItemType Directory -Force $serverDir, $exportDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'server\*') $serverDir -Force
# Aus dem Internet geladene Dateien entsperren (Mark of the Web)
Get-ChildItem $serverDir | Unblock-File
$tiaJson = Join-Path $exportDir 'tia.json'
if (-not (Test-Path $tiaJson)) { Copy-Item (Join-Path $PSScriptRoot 'Project\tia.json') $tiaJson }

Step 'Umgebung pruefen (--doctor)'
& $exe --tia-major-version $tiaVersion --doctor

$serverArgs = @('--tia-major-version', "$tiaVersion", '--software-path', $SoftwarePath, '--export-path', $exportDir, '--keep-structure')
if (-not $ReadOnly) { $serverArgs += '--allow-write' }

if ($RegisterClaudeCode) {
  Step "In Claude Code anmelden (Server '$ServerName', Scope user)"
  if (-not (Get-Command claude -ErrorAction SilentlyContinue)) { throw "'claude' nicht im PATH. Config von Hand eintragen (siehe LIESMICH.md)." }
  claude mcp remove -s user $ServerName 2>$null | Out-Null
  claude mcp add -s user $ServerName -- $exe @serverArgs
}

Step 'Fertig'
Write-Host "Server: $exe"
Write-Host "Export: $exportDir"
Write-Host "`nMCP-Eintrag fuer andere Clients:"
[ordered]@{ mcpServers = [ordered]@{ $ServerName = [ordered]@{ command = $exe; args = $serverArgs } } } | ConvertTo-Json -Depth 5

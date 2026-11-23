<#
.SYNOPSIS
  Baut den TIA-MCP-Server, kopiert ihn in einen Installationsordner und meldet ihn optional in Claude Code an.

.EXAMPLE
  # Standard: TIA V20, Installation nach %USERPROFILE%\TiaMcp, Schreibmodus an, in Claude Code anmelden
  .\tools\Install-TiaMcp.ps1 -RegisterClaudeCode

.EXAMPLE
  # Nur lesen, anderer Ordner, keine Anmeldung
  .\tools\Install-TiaMcp.ps1 -InstallDir D:\Tools\TiaMcp -ReadOnly
#>
param(
  [int]$TiaVersion = 20,
  [string]$InstallDir = (Join-Path $env:USERPROFILE 'TiaMcp'),
  [string]$SoftwarePath = 'PLC_1',
  [switch]$ReadOnly,
  [switch]$RegisterClaudeCode,
  [string]$ServerName = 'vscode-tiaportal-mcp'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$serverDir = Join-Path $InstallDir 'server'
$exportDir = Join-Path $InstallDir 'Project'
$exe = Join-Path $serverDir 'TiaMcpServer.exe'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

Step 'Voraussetzungen pruefen'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK nicht gefunden (https://dotnet.microsoft.com/download).' }
$tiaDll = "C:\Program Files\Siemens\Automation\Portal V$TiaVersion\PublicAPI\V$TiaVersion\Siemens.Engineering.dll"
if (-not (Test-Path $tiaDll)) { throw "TIA Portal V$TiaVersion Openness nicht gefunden: $tiaDll" }
$inGroup = ([Security.Principal.WindowsIdentity]::GetCurrent().Groups | ForEach-Object {
    try { $_.Translate([Security.Principal.NTAccount]).Value } catch { } }) -match '\\Siemens TIA Openness$'
if (-not $inGroup) {
  Write-Warning "Der Benutzer ist nicht in der Gruppe 'Siemens TIA Openness'. Ein Administrator muss ihn hinzufuegen, danach ab- und wieder anmelden."
}

Step "Bauen (TIA V$TiaVersion)"
$running = Get-Process TiaMcpServer -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) { throw "TiaMcpServer.exe laeuft noch aus $serverDir. Claude schliessen oder Kill-TiaMcpServers.bat ausfuehren." }
dotnet build (Join-Path $repo 'src\TiaMcpServer\TiaMcpServer.csproj') -c Release -p:TiaVersion=$TiaVersion -o $serverDir
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
New-Item -ItemType Directory -Force $exportDir | Out-Null
$tiaJson = Join-Path $exportDir 'tia.json'
if (-not (Test-Path $tiaJson)) { Copy-Item (Join-Path $repo 'samples\tia.json') $tiaJson }

Step 'Umgebung pruefen (--doctor)'
& $exe --tia-major-version $TiaVersion --doctor

$serverArgs = @('--tia-major-version', "$TiaVersion", '--software-path', $SoftwarePath, '--export-path', $exportDir, '--keep-structure')
if (-not $ReadOnly) { $serverArgs += '--allow-write' }

if ($RegisterClaudeCode) {
  Step "In Claude Code anmelden (Server '$ServerName', Scope user)"
  if (-not (Get-Command claude -ErrorAction SilentlyContinue)) { throw "'claude' nicht im PATH. Config von Hand eintragen (siehe README)." }
  claude mcp remove -s user $ServerName 2>$null | Out-Null
  claude mcp add -s user $ServerName -- $exe @serverArgs
}

Step 'Fertig'
Write-Host "Server: $exe"
Write-Host "Export: $exportDir"
Write-Host "`nMCP-Eintrag fuer andere Clients:"
[ordered]@{ mcpServers = [ordered]@{ $ServerName = [ordered]@{ command = $exe; args = $serverArgs } } } | ConvertTo-Json -Depth 5

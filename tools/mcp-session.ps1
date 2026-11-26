<#
  Persistente MCP-Testsitzung fuer den TIA-MCP-Server (ohne Claude).
  Startet den Server EINMAL (=> TIA-Zugriffsdialog nur einmal bestaetigen) und arbeitet
  danach Auftraege aus einem Warteschlangen-Ordner ab.

  Start (im Hintergrund):
    .\mcp-session.ps1 -ConfigPath .\mcp.json -QueueDir C:\Temp\mcpq
  Aufruf eines Tools (blockiert bis Antwort):
    .\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Tool GetBlocks -ArgsJson '{}'
  Beenden: Datei 'stop' im QueueDir anlegen (oder .\mcp-call.ps1 -Stop).
  -AutoConfirm: meldet Dialog-Unterstuetzung (MCP elicitation) und beantwortet Bestaetigungsdialoge mit
  "Yes, import"; die Antwort zaehlt sie als "[Rueckfragen: n]" (Test fuer --confirm-writes).
#>
param(
  [string]$ConfigPath = "$PSScriptRoot\mcp.json",
  [Parameter(Mandatory)][string]$QueueDir,
  [int]$TimeoutSec = 900,
  # Meldet MCP-Elicitation und beantwortet Bestaetigungsdialoge des Servers mit "Yes, import"
  [switch]$AutoConfirm
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $QueueDir | Out-Null
Get-ChildItem $QueueDir -File | Remove-Item -Force

$cfg = (Get-Content $ConfigPath -Raw | ConvertFrom-Json).mcpServers.'vscode-tiaportal-mcp'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $cfg.command
$psi.Arguments = (@($cfg.args) | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [Text.Encoding]::UTF8
$proc = [System.Diagnostics.Process]::Start($psi)
$stderrLog = Join-Path $QueueDir 'server.stderr.log'
$errTask = $proc.StandardError.ReadToEndAsync()
$global:id = 0
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Send($method, $params, [bool]$notify = $false) {
  $m = @{ jsonrpc = '2.0'; method = $method }
  if ($null -ne $params) { $m.params = $params }
  if (-not $notify) { $global:id++; $m.id = $global:id }
  $proc.StandardInput.WriteLine(($m | ConvertTo-Json -Depth 30 -Compress)); $proc.StandardInput.Flush()
  if ($notify) { return $null }
  $want = $global:id
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
  while ([DateTime]::UtcNow -lt $deadline) {
    $t = $proc.StandardOutput.ReadLineAsync()
    while (-not $t.Wait(500)) { if ($proc.HasExited -or [DateTime]::UtcNow -gt $deadline) { break } }
    if (-not $t.IsCompleted) { throw "Timeout oder Server beendet" }
    $l = $t.Result; if ($null -eq $l) { throw "EOF vom Server" }
    if (-not $l.Trim().StartsWith('{')) { continue }
    $o = $l | ConvertFrom-Json
    if ($o.method -and $null -ne $o.id) {
      # Anfrage des Servers an den Client (z. B. elicitation/create)
      $global:elicitations++
      $ans = if ($AutoConfirm -and $o.method -eq 'elicitation/create') {
        @{ jsonrpc = '2.0'; id = $o.id; result = @{ action = 'accept'; content = @{ confirmation = @('Yes, import') } } }
      } else { @{ jsonrpc = '2.0'; id = $o.id; result = @{ action = 'decline' } } }
      $proc.StandardInput.WriteLine(($ans | ConvertTo-Json -Depth 10 -Compress)); $proc.StandardInput.Flush()
      continue
    }
    if ($o.id -eq $want) { return $o }
  }
  throw "Timeout"
}

try {
  $r = Send 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = $(if ($AutoConfirm) { @{ elicitation = @{} } } else { @{} }); clientInfo = @{ name = 'ps-session'; version = '1' } }
  [IO.File]::WriteAllText((Join-Path $QueueDir 'ready'), ($r.result | ConvertTo-Json -Depth 10), $utf8)
  Send 'notifications/initialized' $null $true | Out-Null
  while ($true) {
    if (Test-Path (Join-Path $QueueDir 'stop')) { break }
    if ($proc.HasExited) { [IO.File]::WriteAllText((Join-Path $QueueDir 'dead'), "exit $($proc.ExitCode)", $utf8); break }
    $req = Get-ChildItem $QueueDir -Filter '*.req.json' | Sort-Object Name | Select-Object -First 1
    if (-not $req) { Start-Sleep -Milliseconds 300; continue }
    $name = $req.Name -replace '\.req\.json$', ''
    $c = Get-Content $req.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    Remove-Item $req.FullName
    $sw = [Diagnostics.Stopwatch]::StartNew(); $global:elicitations = 0
    try {
      if ($c.method) { $r = Send $c.method $c.params }
      else { $r = Send 'tools/call' @{ name = $c.tool; arguments = $c.args } }
      $out = if ($r.error) { "ERROR: " + ($r.error | ConvertTo-Json -Compress -Depth 10) }
             elseif ($c.method) { $r.result | ConvertTo-Json -Depth 30 }
             else { (($(if ($r.result.isError) { '(isError) ' } else { '' })) + (($r.result.content | ForEach-Object { $_.text }) -join "`n")) }
    } catch { $out = "CLIENT-EXCEPTION: $($_.Exception.Message)" }
    [IO.File]::WriteAllText((Join-Path $QueueDir "$name.tmp"), "[$([int]$sw.Elapsed.TotalSeconds)s]$(if ($global:elicitations) { ' [Rueckfragen: ' + $global:elicitations + ']' }) $out", $utf8)
    Move-Item (Join-Path $QueueDir "$name.tmp") (Join-Path $QueueDir "$name.res.txt") -Force
  }
} finally {
  try { $proc.StandardInput.Close() } catch {}
  if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
  [IO.File]::WriteAllText($stderrLog, $errTask.Result, $utf8)
  Remove-Item (Join-Path $QueueDir 'ready') -ErrorAction SilentlyContinue
}

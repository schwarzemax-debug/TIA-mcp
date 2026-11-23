<#
  Minimaler MCP-Stdio-Client fuer den TIA-MCP-Server (zum Testen ohne Claude/Copilot).
  Aufruf:
    .\mcp-client.ps1 -Calls @(
      @{ tool='GetStates'; args=@{} },
      @{ tool='GetProjectInfo'; args=@{} }
    ) [-AllowWrite] [-ListTools]
  Startet den Server mit den Argumenten aus .mcp.json (gleiche Config wie Claude Code),
  fuehrt initialize + tools/call aus und gibt die Antworten als Text aus.
#>
param(
  [object[]]$Calls = @(),
  [switch]$ListTools,
  [switch]$AllowWrite,
  [string]$ConfigPath = "$PSScriptRoot\mcp.json",
  [int]$TimeoutSec = 180
)
$cfg = (Get-Content $ConfigPath -Raw | ConvertFrom-Json).mcpServers.'vscode-tiaportal-mcp'
$argList = @($cfg.args)
if ($AllowWrite -and ($argList -notcontains '--allow-write')) { $argList += '--allow-write' }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $cfg.command
$psi.Arguments = ($argList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$proc = [System.Diagnostics.Process]::Start($psi)
$errTask = $proc.StandardError.ReadToEndAsync()
$global:id = 0
function Send($method, $params, [bool]$notify = $false) {
  $m = @{ jsonrpc = '2.0'; method = $method }
  if ($params) { $m.params = $params }
  if (-not $notify) { $global:id++; $m.id = $global:id }
  $line = ($m | ConvertTo-Json -Depth 20 -Compress)
  $proc.StandardInput.WriteLine($line); $proc.StandardInput.Flush()
  if ($notify) { return $null }
  $want = $global:id
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
  while ([DateTime]::UtcNow -lt $deadline) {
    $t = $proc.StandardOutput.ReadLineAsync()
    while (-not $t.Wait(500)) { if ([DateTime]::UtcNow -gt $deadline -or $proc.HasExited) { break } }
    if (-not $t.IsCompleted) { throw "Timeout/Prozess beendet. stderr: " + $errTask.Result }
    $l = $t.Result; if ($null -eq $l) { throw "EOF. stderr: " + $errTask.Result }
    if (-not $l.Trim().StartsWith('{')) { continue }
    $o = $l | ConvertFrom-Json
    if ($o.id -eq $want) { return $o }
  }
  throw "Timeout"
}
try {
  $r = Send 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'ps-test'; version = '1' } }
  "== initialize: " + $r.result.serverInfo.name + " " + $r.result.serverInfo.version
  Send 'notifications/initialized' $null $true | Out-Null
  if ($ListTools) {
    $r = Send 'tools/list' @{}
    $r.result.tools | ForEach-Object { "{0} :: {1}" -f $_.name, (($_.inputSchema.properties.PSObject.Properties.Name) -join ',') }
  }
  foreach ($c in $Calls) {
    "`n== CALL $($c.tool) $($c.args | ConvertTo-Json -Compress -Depth 10)"
    $r = Send 'tools/call' @{ name = $c.tool; arguments = $c.args }
    if ($r.error) { "ERROR: " + ($r.error | ConvertTo-Json -Compress) }
    else { if ($r.result.isError) { "(isError)" }; $r.result.content | ForEach-Object { $_.text } }
  }
} finally {
  try { $proc.StandardInput.Close() } catch {}
  if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
  "`n== stderr:"; $errTask.Result
}

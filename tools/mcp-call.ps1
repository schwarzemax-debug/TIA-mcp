<#
  Schickt einen Tool-Aufruf an eine laufende mcp-session.ps1 und wartet auf die Antwort.
    .\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Tool GetBlocks -ArgsJson '{"regexName":"FB_"}'
    .\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Method tools/list
    .\mcp-call.ps1 -QueueDir C:\Temp\mcpq -Stop
#>
param(
  [Parameter(Mandatory)][string]$QueueDir,
  [string]$Tool,
  [string]$ArgsJson = '{}',
  [string]$Method,
  [int]$TimeoutSec = 900,
  [switch]$Stop
)
$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($Stop) { [IO.File]::WriteAllText((Join-Path $QueueDir 'stop'), 'stop', $utf8); 'stop requested'; return }
if (-not (Test-Path (Join-Path $QueueDir 'ready'))) { throw "Keine laufende Sitzung in $QueueDir" }
$name = (Get-Date).ToString('yyyyMMddHHmmssfff') + '_' + ($(if ($Tool) { $Tool } else { $Method -replace '/', '_' }))
$body = if ($Method) { @{ method = $Method; params = @{} } } else { @{ tool = $Tool; args = ($ArgsJson | ConvertFrom-Json) } }
[IO.File]::WriteAllText((Join-Path $QueueDir "$name.tmpreq"), ($body | ConvertTo-Json -Depth 30), $utf8)
Move-Item (Join-Path $QueueDir "$name.tmpreq") (Join-Path $QueueDir "$name.req.json")
$res = Join-Path $QueueDir "$name.res.txt"
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not (Test-Path $res)) {
  if ((Get-Date) -gt $deadline) { throw "Timeout ($TimeoutSec s) fuer $name" }
  if (Test-Path (Join-Path $QueueDir 'dead')) { throw "Server beendet: " + (Get-Content (Join-Path $QueueDir 'dead') -Raw) }
  Start-Sleep -Milliseconds 300
}
Get-Content $res -Raw -Encoding UTF8

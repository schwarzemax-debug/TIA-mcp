<#
.SYNOPSIS
  Baut das Release-Paket (ZIP mit server\, Install.ps1, LIESMICH.md, Vorlagen) nach artifacts\.

.EXAMPLE
  .\tools\New-ReleasePackage.ps1 -Version 0.4.0
#>
param(
  [string]$Version = '0.4.0',
  [int]$TiaVersion = 20,
  [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$name = "TIA-mcp-v$Version-tia$TiaVersion"
$stage = Join-Path $OutDir $name
$zip = Join-Path $OutDir "$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'Project'), (Join-Path $stage 'samples') | Out-Null

dotnet build (Join-Path $repo 'src\TiaMcpServer\TiaMcpServer.csproj') -c Release -p:TiaVersion=$TiaVersion -o (Join-Path $stage 'server')
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

Copy-Item (Join-Path $repo 'packaging\Install.ps1'), (Join-Path $repo 'packaging\LIESMICH.md'),
          (Join-Path $repo 'LICENSE.txt'), (Join-Path $repo 'Kill-TiaMcpServers.bat') $stage
Copy-Item (Join-Path $repo 'samples\tia.json') (Join-Path $stage 'Project')
foreach ($s in 'claude', 'claude-code', 'vscode') { Copy-Item (Join-Path $repo "samples\$s") (Join-Path $stage 'samples') -Recurse -Force }

# Eintraege selbst schreiben: Compress-Archive/ZipFile unter .NET 4.8 setzen '\' in die Namen (nicht standardkonform)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
  Get-ChildItem $stage -Recurse -File -Force | ForEach-Object {
    $entry = $name + '/' + $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $archive.Dispose() }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Paket: $zip"
Write-Host "SHA256: $hash"

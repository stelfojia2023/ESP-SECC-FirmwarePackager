param([string]$EvidenceDirectory = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $env:TEMP ('secc-local-protocol-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$exe = Join-Path $EvidenceDirectory 'LocalUpgradeProtocolTests.exe'
$sources = @(Get-ChildItem -Path (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName })
& $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:LocalUpgradeProtocolTests "/out:$exe" $sources (Join-Path $root 'tests\LocalUpgradeProtocolTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Local upgrade protocol test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Local upgrade protocol tests failed.' }

param([string]$EvidenceDirectory = "", [string]$Case = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $env:TEMP ('secc-local-ui-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$exe = Join-Path $EvidenceDirectory 'LocalUpgradeUiTests.exe'
$sources = @(Get-ChildItem -Path (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName })
& $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:LocalUpgradeUiTests "/out:$exe" $sources (Join-Path $root 'tests\LocalUpgradeUiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Local upgrade UI test compilation failed.' }
& $exe (Join-Path $root 'test-vectors\bundle-m4-esp32-v1.bin') $Case
if ($LASTEXITCODE -ne 0) { throw 'Local upgrade UI tests failed.' }

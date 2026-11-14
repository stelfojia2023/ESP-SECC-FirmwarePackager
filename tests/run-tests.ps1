$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('secc-packager-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$core = Join-Path $root 'src\Packager.cs'
$form = Join-Path $root 'src\MainForm.cs'
$coreTest = Join-Path $root 'tests\CoreTests.cs'
$guiTest = Join-Path $root 'tests\GuiSmokeTests.cs'
$releaseTest = Join-Path $root 'tests\ReleaseUiTests.cs'
$coreExe = Join-Path $testRoot 'CoreTests.exe'
$guiExe = Join-Path $testRoot 'GuiSmokeTests.exe'
$releaseExe = Join-Path $root 'dist\FirmwarePackager.exe'
$releaseTestExe = Join-Path $testRoot 'ReleaseUiTests.exe'
if (!(Test-Path -LiteralPath $releaseExe)) { throw 'Build dist\FirmwarePackager.exe before running the tests.' }
& $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$coreExe" $core $coreTest
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
& $coreExe (Join-Path $testRoot 'core results')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:GuiSmokeTests "/out:$guiExe" $core $form $guiTest
if ($LASTEXITCODE -ne 0) { throw 'GUI test compilation failed.' }
& $guiExe (Join-Path $testRoot 'gui results')
if ($LASTEXITCODE -ne 0) { throw 'GUI smoke test failed.' }
& $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$releaseTestExe" $core $releaseTest
if ($LASTEXITCODE -ne 0) { throw 'Release GUI test compilation failed.' }
& $releaseTestExe $releaseExe (Join-Path $testRoot 'release gui results')
if ($LASTEXITCODE -ne 0) { throw 'Release GUI test failed.' }
Write-Host "Test evidence: $testRoot"
Write-Host 'Run the independent Python verifier separately as described in README.md.'

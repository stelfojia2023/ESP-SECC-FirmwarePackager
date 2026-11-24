param(
    [string[]]$Only = @(),
    [switch]$SkipPreflight
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$started = Get-Date
$allCases = @('filename', 'header', 'core', 'format', 'ftp', 'gui', 'local-upgrade-protocol',
    'local-upgrade-ui', 'release-ui', 'release-ftp', 'appearance')
$selected = @($Only | Where-Object { $_ } | ForEach-Object { $_.Trim().ToLowerInvariant() })
if ($selected.Count -eq 0) { $selected = $allCases }
foreach ($case in $selected) {
    if ($allCases -notcontains $case) {
        throw "Unknown test case: $case. Use one of: $($allCases -join ', ')"
    }
}

function Wanted([string]$name) { return $selected -contains $name }
function Complete([string]$name, [DateTime]$stepStart) {
    Write-Host ("DONE " + $name + " (" + [int]((Get-Date) - $stepStart).TotalSeconds + "s)")
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('secc-packager-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$env:SECC_PACKAGER_SETTINGS_DIR = Join-Path $testRoot 'appearance-isolation'
$core = Join-Path $root 'src\Packager.cs'
$ftp = Join-Path $root 'src\FtpUploader.cs'
$form = Join-Path $root 'src\MainForm.cs'
$upgrade = Join-Path $root 'src\UpgradePage.cs'
$localUpgrade = Join-Path $root 'src\LocalUpgrade.cs'
$upgradeTimeout = Join-Path $root 'src\UpgradeTimeoutDialog.cs'
$header = Join-Path $root 'src\HeaderViewer.cs'
$names = Join-Path $root 'src\FilenameRules.cs'
$ui = Join-Path $root 'src\UiControls.cs'
$testSupport = Join-Path $root 'tests\TestSupport.cs'
$coreTest = Join-Path $root 'tests\CoreTests.cs'
$guiTest = Join-Path $root 'tests\GuiSmokeTests.cs'
$ftpTest = Join-Path $root 'tests\FtpTests.cs'
$releaseTest = Join-Path $root 'tests\ReleaseUiTests.cs'
$coreExe = Join-Path $testRoot 'CoreTests.exe'
$guiExe = Join-Path $testRoot 'GuiSmokeTests.exe'
$ftpExe = Join-Path $testRoot 'FtpTests.exe'
$releaseExe = Join-Path $root 'dist\FirmwarePackager.exe'
$releaseTestExe = Join-Path $testRoot 'ReleaseUiTests.exe'
$icon = Join-Path $root 'assets\linchr-mascot.ico'
$previousSample = Join-Path $root 'test-vectors\bundle-m4-esp32-v1.bin'
if (!(Test-Path -LiteralPath $releaseExe)) { throw 'Build dist\FirmwarePackager.exe before running the tests.' }
if (!(Test-Path -LiteralPath $previousSample)) { throw 'The previous V1 test vector was not found.' }

$interactive = @(@('gui', 'release-ui', 'release-ftp') | Where-Object { Wanted $_ })
if (!$SkipPreflight -and $interactive.Count -gt 0) {
    $preflightExe = Join-Path $testRoot 'ClipboardPreflight.exe'
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll "/out:$preflightExe" (Join-Path $root 'tests\ClipboardPreflight.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard preflight compilation failed.' }
    & $preflightExe 10
}

if (Wanted 'filename') {
    $step = Get-Date
    $filenameExe = Join-Path $testRoot 'FilenameRulesTests.exe'
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$filenameExe" $core $names (Join-Path $root 'tests\FilenameRulesTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Filename test compilation failed.' }
    & $filenameExe
    if ($LASTEXITCODE -ne 0) { throw 'Filename tests failed.' }
    Complete 'filename' $step
}

if (Wanted 'header') {
    $step = Get-Date
    $headerExe = Join-Path $testRoot 'HeaderViewerTests.exe'
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:HeaderViewerTests "/out:$headerExe" $core $ftp $form $upgrade $localUpgrade $upgradeTimeout $ui $names (Join-Path $root 'src\HeaderViewer.cs') (Join-Path $root 'tests\HeaderViewerTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Header viewer compilation failed.' }
    & $headerExe (Join-Path $testRoot 'header results')
    if ($LASTEXITCODE -ne 0) { throw 'Header viewer tests failed.' }
    Complete 'header' $step
}

if (Wanted 'core') {
    $step = Get-Date
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$coreExe" $core $coreTest
    if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
    & $coreExe (Join-Path $testRoot 'core results')
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    Complete 'core' $step
}

if (Wanted 'format') {
    $step = Get-Date
    $formatExe = Join-Path $testRoot 'NewFormatContractTests.exe'
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$formatExe" $core (Join-Path $root 'tests\NewFormatContractTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'New format contract compilation failed.' }
    & $formatExe (Join-Path $testRoot 'new format contract') (Join-Path $root 'tests\fixtures\legacy-v1-directory-0x80.bin')
    if ($LASTEXITCODE -ne 0) { throw 'New format contract tests failed.' }
    Complete 'format' $step
}

if (Wanted 'ftp') {
    $step = Get-Date
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /main:FtpTests "/out:$ftpExe" $core $ftp $ftpTest
    if ($LASTEXITCODE -ne 0) { throw 'FTP test compilation failed.' }
    & $ftpExe $previousSample (Join-Path $testRoot 'ftp results') (Join-Path $root 'tests\fixtures\legacy-v1-directory-0x80.bin')
    if ($LASTEXITCODE -ne 0) { throw 'FTP tests failed.' }
    Complete 'ftp' $step
}

if (Wanted 'gui') {
    $step = Get-Date
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:GuiSmokeTests "/win32icon:$icon" "/out:$guiExe" $core $ftp $form $upgrade $localUpgrade $upgradeTimeout $ui $names $header $ftpTest $testSupport $guiTest
    if ($LASTEXITCODE -ne 0) { throw 'GUI test compilation failed.' }
    & $guiExe (Join-Path $testRoot 'gui results')
    if ($LASTEXITCODE -ne 0) { throw 'GUI smoke test failed.' }
    Complete 'gui' $step
}

if (Wanted 'local-upgrade-protocol') {
    $step = Get-Date
    & (Join-Path $root 'tests\run-local-upgrade-protocol.ps1') -EvidenceDirectory (Join-Path $testRoot 'local-upgrade-protocol')
    Complete 'local-upgrade-protocol' $step
}

if (Wanted 'local-upgrade-ui') {
    $step = Get-Date
    & (Join-Path $root 'tests\run-local-upgrade-ui.ps1') -EvidenceDirectory (Join-Path $testRoot 'local-upgrade-ui')
    Complete 'local-upgrade-ui' $step
}

if (Wanted 'release-ui') {
    $step = Get-Date
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$releaseTestExe" $core $testSupport $releaseTest
    if ($LASTEXITCODE -ne 0) { throw 'Release GUI test compilation failed.' }
    & $releaseTestExe $releaseExe (Join-Path $testRoot 'release gui results') $previousSample
    if ($LASTEXITCODE -ne 0) { throw 'Release GUI test failed.' }
    Complete 'release-ui' $step
}

if (Wanted 'release-ftp') {
    $step = Get-Date
    $releaseFtpTestExe = Join-Path $testRoot 'ReleaseFtpDirectoryTests.exe'
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /main:ReleaseFtpDirectoryTests "/out:$releaseFtpTestExe" $core $ftp $ftpTest (Join-Path $root 'tests\ReleaseFtpDirectoryTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Standalone FTP directory test compilation failed.' }
    $standalone = Join-Path $testRoot 'standalone'
    New-Item -ItemType Directory -Path $standalone | Out-Null
    $standaloneExe = Join-Path $standalone 'FirmwarePackager.exe'
    Copy-Item -LiteralPath $releaseExe -Destination $standaloneExe
    & $releaseFtpTestExe $standaloneExe
    if ($LASTEXITCODE -ne 0) { throw 'Standalone FTP directory test failed.' }
    Complete 'release-ftp' $step
}

if (Wanted 'appearance') {
    $step = Get-Date
    $appearanceExe = Join-Path $testRoot 'AppearanceTests.exe'
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$appearanceExe" (Join-Path $root 'tests\AppearanceTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Appearance test compilation failed.' }
    & $appearanceExe $releaseExe (Join-Path $testRoot 'appearance results')
    if ($LASTEXITCODE -ne 0) { throw 'Appearance tests failed.' }
    Complete 'appearance' $step
}

Write-Host ("Test evidence: " + $testRoot + " (total " + [int]((Get-Date) - $started).TotalSeconds + "s)")
Write-Host 'Run the independent Python verifier separately as described in README.md.'

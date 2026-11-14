param(
    [Parameter(Mandatory = $true)][string]$Groups,
    [string]$EvidenceDirectory = "",
    [switch]$RunDesktop,
    [string]$BaselineExe = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allowed = @('filename', 'core', 'ftp', 'header', 'ui', 'appearance', 'typography', 'vector', 'publisher', 'desktop')
$selected = @($Groups.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
if ($selected -contains 'all') { $selected = $allowed }
if ($selected.Count -eq 0) { throw 'Select at least one test group.' }
foreach ($group in $selected) {
    if ($allowed -notcontains $group) { throw "Unknown test group: $group" }
}
if (($RunDesktop -or $BaselineExe) -and ($selected -notcontains 'desktop')) {
    throw 'RunDesktop and BaselineExe require the desktop group.'
}
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $env:TEMP ('secc-selected-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$nativeGroups = @('filename', 'core', 'ftp', 'header', 'ui', 'appearance', 'typography', 'desktop')
$needsBuild = @($selected | Where-Object { $nativeGroups -contains $_ }).Count -gt 0
if ($needsBuild -and !(Test-Path -LiteralPath $csc)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
$oldSettings = [Environment]::GetEnvironmentVariable('SECC_PACKAGER_SETTINGS_DIR', 'Process')
$env:SECC_PACKAGER_SETTINGS_DIR = Join-Path $EvidenceDirectory 'settings'
Push-Location $root
try {
    if ($needsBuild) {
        & (Join-Path $root 'build.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Windows EXE build failed.' }
    }
    $core = Join-Path $root 'src\Packager.cs'
    $ftp = Join-Path $root 'src\FtpUploader.cs'
    $names = Join-Path $root 'src\FilenameRules.cs'
    $releaseExe = Join-Path $root 'dist\FirmwarePackager.exe'
    $sources = @(Get-ChildItem -Path (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName })
    if ($selected -contains 'filename') {
        $exe = Join-Path $EvidenceDirectory 'FilenameRulesTests.exe'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$exe" $core $names (Join-Path $root 'tests\FilenameRulesTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Filename test compilation failed.' }
        & $exe
        if ($LASTEXITCODE -ne 0) { throw 'Filename tests failed.' }
    }
    if ($selected -contains 'core') {
        $exe = Join-Path $EvidenceDirectory 'CoreTests.exe'
        $coreResults = Join-Path $EvidenceDirectory 'core-results'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 "/out:$exe" $core (Join-Path $root 'tests\CoreTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
        & $exe $coreResults
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
        python (Join-Path $root 'tests\independent_verify.py') $coreResults
        if ($LASTEXITCODE -ne 0) { throw 'Independent format verification failed.' }
    }
    if ($selected -contains 'ftp') {
        $exe = Join-Path $EvidenceDirectory 'FtpTests.exe'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /main:FtpTests "/out:$exe" $core $ftp (Join-Path $root 'tests\FtpTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'FTP test compilation failed.' }
        & $exe (Join-Path $root 'test-vectors\bundle-m4-esp32-v1.bin') (Join-Path $EvidenceDirectory 'ftp-results')
        if ($LASTEXITCODE -ne 0) { throw 'FTP tests failed.' }
    }
    if ($selected -contains 'header') {
        $exe = Join-Path $EvidenceDirectory 'HeaderViewerTests.exe'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:HeaderViewerTests "/out:$exe" $sources (Join-Path $root 'tests\HeaderViewerTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Header viewer compilation failed.' }
        & $exe (Join-Path $EvidenceDirectory 'header-results')
        if ($LASTEXITCODE -ne 0) { throw 'Header viewer tests failed.' }
    }
    if ($selected -contains 'ui') {
        $exe = Join-Path $EvidenceDirectory 'UiContractTests.exe'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$exe" (Join-Path $root 'tests\UiContractTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'UI contract compilation failed.' }
        & $exe $releaseExe (Join-Path $EvidenceDirectory 'ui-results')
        if ($LASTEXITCODE -ne 0) { throw 'UI contract tests failed.' }
    }
    if ($selected -contains 'appearance') {
        $exe = Join-Path $EvidenceDirectory 'AppearanceTests.exe'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$exe" (Join-Path $root 'tests\AppearanceTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Appearance test compilation failed.' }
        & $exe $releaseExe (Join-Path $EvidenceDirectory 'appearance-results')
        if ($LASTEXITCODE -ne 0) { throw 'Appearance tests failed.' }
    }
    if ($selected -contains 'typography') {
        foreach ($mode in @('aware', 'baseline')) {
            $exe = Join-Path $EvidenceDirectory ("Typography-" + $mode + ".exe")
            $manifest = if ($mode -eq 'aware') { "/win32manifest:$(Join-Path $root 'app.manifest')" } else { '/nowin32manifest' }
            & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll $manifest "/out:$exe" (Join-Path $root 'tests\TypographyTests.cs')
            if ($LASTEXITCODE -ne 0) { throw "Typography $mode compilation failed." }
            & $exe $releaseExe (Join-Path $EvidenceDirectory ("typography-" + $mode))
            if ($LASTEXITCODE -ne 0) { throw "Typography $mode diagnostics failed." }
        }
    }
    if ($selected -contains 'vector') {
        python (Join-Path $root 'tests\verify_vector.py') (Join-Path $root 'test-vectors')
        if ($LASTEXITCODE -ne 0) { throw 'Reference vector verification failed.' }
    }
    if ($selected -contains 'publisher') {
        python (Join-Path $root 'tests\test_release_publisher.py')
        if ($LASTEXITCODE -ne 0) { throw 'Release publisher safeguards failed.' }
    }
    if ($selected -contains 'desktop') {
        $gui = Join-Path $EvidenceDirectory 'GuiSmokeTests.exe'
        $release = Join-Path $EvidenceDirectory 'ReleaseUiTests.exe'
        $directory = Join-Path $EvidenceDirectory 'ReleaseFtpDirectoryTests.exe'
        $icon = Join-Path $EvidenceDirectory 'IconPreservationTests.exe'
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /main:GuiSmokeTests "/win32icon:$(Join-Path $root 'assets\linchr-mascot.ico')" "/out:$gui" $sources (Join-Path $root 'tests\FtpTests.cs') (Join-Path $root 'tests\GuiSmokeTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Desktop GUI test compilation failed.' }
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$release" $core (Join-Path $root 'tests\ReleaseUiTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Native release test compilation failed.' }
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /main:ReleaseFtpDirectoryTests "/out:$directory" $core $ftp (Join-Path $root 'tests\FtpTests.cs') (Join-Path $root 'tests\ReleaseFtpDirectoryTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Desktop FTP test compilation failed.' }
        & $csc /nologo /codepage:65001 /target:exe /platform:x64 /reference:System.Drawing.dll "/out:$icon" (Join-Path $root 'tests\IconPreservationTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Icon comparison compilation failed.' }
        if ($RunDesktop) {
            & $gui (Join-Path $EvidenceDirectory 'gui-results')
            if ($LASTEXITCODE -ne 0) { throw 'Desktop GUI tests failed.' }
            & $release $releaseExe (Join-Path $EvidenceDirectory 'release-ui-results') (Join-Path $root 'test-vectors\bundle-m4-esp32-v1.bin')
            if ($LASTEXITCODE -ne 0) { throw 'Native release UI tests failed.' }
            $standalone = Join-Path $EvidenceDirectory ('standalone-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $standalone | Out-Null
            $standaloneExe = Join-Path $standalone 'FirmwarePackager.exe'
            Copy-Item -LiteralPath $releaseExe -Destination $standaloneExe
            & $directory $standaloneExe
            if ($LASTEXITCODE -ne 0) { throw 'Desktop FTP tests failed.' }
            Write-Host 'PASS native interactive desktop tests'
        }
        else { Write-Host 'NOT_RUN native desktop interaction: compiled only; use -RunDesktop on an unlocked Windows desktop.' }
        if ($BaselineExe) {
            & $icon $BaselineExe $releaseExe
            if ($LASTEXITCODE -ne 0) { throw 'Icon comparison failed.' }
        }
        else { Write-Host 'NOT_RUN icon comparison: supply -BaselineExe when verifying icon preservation.' }
    }
    $executed = @($selected | Where-Object { $_ -ne 'desktop' })
    if ($executed.Count -gt 0) { Write-Host ("Automated groups passed: " + ($executed -join ', ')) }
    Write-Host ("Test evidence: " + $EvidenceDirectory)
}
finally {
    [Environment]::SetEnvironmentVariable('SECC_PACKAGER_SETTINGS_DIR', $oldSettings, 'Process')
    Pop-Location
}

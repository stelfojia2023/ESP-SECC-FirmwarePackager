$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$output = Join-Path $dist 'FirmwarePackager.exe'
$icon = Join-Path $root 'assets\linchr-mascot.ico'
if (!(Test-Path -LiteralPath $icon)) { throw 'The application icon was not found.' }
$sources = @((Join-Path $root 'src\Packager.cs'), (Join-Path $root 'src\FtpUploader.cs'), (Join-Path $root 'src\MainForm.cs'), (Join-Path $root 'src\UiControls.cs'), (Join-Path $root 'src\FilenameRules.cs'), (Join-Path $root 'src\HeaderViewer.cs'))
$resources = @('click', 'success', 'failure') | ForEach-Object { '/resource:' + (Join-Path $root ('assets\sounds\' + $_ + '.wav')) + ',Sounds.' + $_ + '.wav' }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$icon" "/out:$output" $resources $sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
Write-Host "Built: $output"
Get-FileHash -Algorithm SHA256 -LiteralPath $output | Format-List Algorithm, Hash, Path


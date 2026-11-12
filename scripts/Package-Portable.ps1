param([string]$Version='1.1.3',[switch]$NoRestore)
$ErrorActionPreference='Stop'
$repositoryRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if($Version -notmatch '^[0-9]+(?:[.][0-9]+){1,3}$'){throw 'Invalid package version'}
$releaseRoot=Join-Path $repositoryRoot ('artifacts\CarArchive-'+$Version)
if(Test-Path -LiteralPath $releaseRoot){$releaseRoot+='-'+(Get-Date -Format 'yyyyMMdd-HHmmss')}
$program=Join-Path $releaseRoot 'Program'
$docs=Join-Path $releaseRoot 'Documentation'
$data=Join-Path $releaseRoot 'Data'
New-Item -ItemType Directory -Path $program,$docs,$data -Force | Out-Null
$publish=@('publish',(Join-Path $repositoryRoot 'CarArchive\CarArchive.csproj'),'-c','Release','-r','win-x64','-p:Platform=x64','--self-contained','true','-p:PublishReadyToRun=false','-p:PublishTrimmed=false','-p:NuGetAudit=false','-o',$program)
if($NoRestore){$publish+='--no-restore'}
& dotnet @publish
if($LASTEXITCODE -ne 0){throw 'CarArchive publish failed'}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $csc)){throw 'Windows .NET Framework compiler not found'}
$launcher=Join-Path $releaseRoot 'CarArchive.exe'
$icon=Join-Path $repositoryRoot 'CarArchive\Assets\AppIcon.ico'
$assemblyVersion=(($Version.Split('.')+@('0','0','0','0'))[0..3] -join '.')
$assemblyInfo=Join-Path ([IO.Path]::GetTempPath()) ('CarArchive-launcher-'+[Guid]::NewGuid().ToString('N')+'.cs')
@"
using System.Reflection;
[assembly: AssemblyTitle("CarArchive")]
[assembly: AssemblyProduct("CarArchive")]
[assembly: AssemblyCompany("MIRAIEVO / Codex / GitHub Copilot")]
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content -LiteralPath $assemblyInfo -Encoding UTF8
$cscArguments=@('/nologo' ,'/codepage:65001','/target:winexe','/platform:x64','/optimize+',('/out:'+$launcher),'/reference:System.Windows.Forms.dll',(Join-Path $repositoryRoot 'CarArchive.Launcher\Launcher.cs'),$assemblyInfo)
if(Test-Path -LiteralPath $icon){$cscArguments+=('/win32icon:'+$icon)}
try { & $csc @cscArguments; $compilerExit=$LASTEXITCODE } finally { Remove-Item -LiteralPath $assemblyInfo -Force }
if($compilerExit -ne 0){throw 'Portable launcher compilation failed'}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CarArchive\README.md') -Destination (Join-Path $docs 'README.md')
foreach($document in @('CHANGELOG.md','AUTHORS.md')) { Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination (Join-Path $docs $document) }
@'
CarArchive portable edition
Run CarArchive.exe in the package root. Keep the Program folder alongside it.
The launcher uses the .NET Framework included with supported Windows; the app is self-contained.
Release 1.1.3: reliable taskbar and title-bar icons, aligned thumbnail selection fill, and independent portable data.
Authors: MIRAIEVO, Codex (OpenAI), GitHub Copilot (Microsoft).
'@ | Set-Content -LiteralPath (Join-Path $docs 'Release-Notes.txt') -Encoding utf8
@'
Each release stores its own data under Data next to the root launcher. This package contains no photos, database or API key.
Close the application before backing up Data. Import previous data using the first-run selector.
Crash reports: Data\crashes. Clean exits discard recent-action checkpoints.
API credentials are DPAPI-protected for the current Windows user.
'@ | Set-Content -LiteralPath (Join-Path $docs 'Data-and-Backup.txt') -Encoding utf8
$zip=Join-Path $repositoryRoot ('artifacts\CarArchive-'+$Version+'-windows-x64.zip')
Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $zip -CompressionLevel Optimal -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Update)
try { if($archive.GetEntry('Data/') -eq $null) { [void]$archive.CreateEntry('Data/') } }
finally { $archive.Dispose() }
$checksum=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
$checksumPath=$zip+'.sha256'
[IO.File]::WriteAllText($checksumPath,"$checksum  $(Split-Path -Leaf $zip)`r`n",[Text.Encoding]::ASCII)
Write-Output ('PackageDirectory: '+$releaseRoot)
Write-Output ('Launcher: '+$launcher)
Write-Output ('Archive: '+$zip)
Write-Output ('SHA256: '+$checksum)

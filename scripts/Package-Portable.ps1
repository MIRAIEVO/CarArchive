param([string]$Version='1.4',[switch]$NoRestore)
$ErrorActionPreference='Stop'
$repositoryRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if($Version -notmatch '^[0-9]+(?:[.][0-9]+){1,3}$'){throw 'Invalid package version'}
$releaseRoot=Join-Path $repositoryRoot ('artifacts\CarArchive-'+$Version)
if(Test-Path -LiteralPath $releaseRoot){$releaseRoot+='-'+(Get-Date -Format 'yyyyMMdd-HHmmss')}
$program=Join-Path $releaseRoot 'Program'
$docs=Join-Path $releaseRoot 'Documentation'
$data=Join-Path $releaseRoot 'Data'
$tool=Join-Path $releaseRoot 'Tools\DuplicateChecker'
New-Item -ItemType Directory -Path $program,$docs,$data -Force | Out-Null
$publish=@('publish',(Join-Path $repositoryRoot 'CarArchive\CarArchive.csproj'),'-c','Release','-r','win-x64','-p:Platform=x64','--self-contained','true','-p:PublishReadyToRun=false','-p:PublishTrimmed=false','-p:NuGetAudit=false','-p:IncludeSourceRevisionInInformationalVersion=false','-o',$program)
if($NoRestore){$publish+='--no-restore'}
& dotnet @publish
if($LASTEXITCODE -ne 0){throw 'CarArchive publish failed'}
if([version]$Version -ge [version]'1.4'){
    $standalone=Join-Path $repositoryRoot ("artifacts\DuplicateChecker-"+$Version+"-windows-x64.zip")
    & (Join-Path $repositoryRoot 'Tools\DuplicateChecker\Publish.ps1') -OutputDirectory $tool -ZipPath $standalone -NoRestore:$NoRestore
}
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
$versionNotes=Join-Path $repositoryRoot ('docs\releases\release-notes-v'+$Version+'.md')
if(Test-Path -LiteralPath $versionNotes) { Copy-Item -LiteralPath $versionNotes -Destination (Join-Path $docs 'Release-Notes.md') }
$namingDocs=Join-Path $docs 'Naming'
New-Item -ItemType Directory -Path $namingDocs -Force | Out-Null
foreach($name in @('命名指南.csv','命名扫描清单.csv','说明与补充要求.md','命名输入推演.csv','命名差异与待复核.csv','品牌资料索引.csv','改款与代次核对.csv','命名学习覆盖.json')) {
    $sourceDocument=Join-Path $repositoryRoot ('CarArchive.Core\Resources\'+$name)
    if(!(Test-Path -LiteralPath $sourceDocument)){throw ('Missing bundled naming document: '+$name)}
    Copy-Item -LiteralPath $sourceDocument -Destination (Join-Path $namingDocs $name)
}

@'
CarArchive portable edition
Run CarArchive.exe in the package root. Keep the Program folder alongside it.
The launcher uses the .NET Framework included with supported Windows; the app is self-contained.
The Toolbox opens its standalone utilities from the Tools directory.
Brand, series and model naming guidance is bundled for offline use; existing archive folders are not renamed.
Authors: MIRAIEVO, Codex (OpenAI), GitHub Copilot (Microsoft).
'@ | Set-Content -LiteralPath (Join-Path $docs 'Release-Notes.txt') -Encoding utf8
@'
Each release stores its own data under Data next to the root launcher. This package contains no photos, database or API key.
Close the application before backing up Data. The first-run selector imports API credentials, settings, naming learning and archive targets only; groups, media caches and operation history are excluded.
Crash reports: Data\crashes. Clean exits discard recent-action checkpoints.
API credentials are DPAPI-protected for the current Windows user.
'@ | Set-Content -LiteralPath (Join-Path $docs 'Data-and-Backup.txt') -Encoding utf8
# Refuse to publish a package carrying workspace data or credentials.
if(@(Get-ChildItem -LiteralPath $data -Force).Count -ne 0){throw 'The package Data directory must be empty.'}
$forbidden=Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Where-Object {
    $_.Name -match '-key[.]bin(?:[.]tmp)?$' -or $_.Name -match '[.]db(?:-shm|-wal)?$'
}
if($forbidden){throw 'Unexpected workspace database or credential in release output.'}
if(@(Get-ChildItem -LiteralPath $releaseRoot -File).Count -ne 1 -or !(Test-Path -LiteralPath $launcher)){throw 'The package root must contain only the launcher EXE.'}
$zip=Join-Path $repositoryRoot ('artifacts\CarArchive-'+$Version+'-windows-x64.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}
[IO.Compression.ZipFile]::CreateFromDirectory($releaseRoot,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$checksum=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
$checksumPath=$zip+'.sha256'
[IO.File]::WriteAllText($checksumPath,"$checksum  $(Split-Path -Leaf $zip)`r`n",[Text.Encoding]::ASCII)
Write-Output ('PackageDirectory: '+$releaseRoot)
Write-Output ('Launcher: '+$launcher)
Write-Output ('Archive: '+$zip)
Write-Output ('SHA256: '+$checksum)

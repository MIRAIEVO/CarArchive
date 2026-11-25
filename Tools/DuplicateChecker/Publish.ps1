param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\DuplicateChecker-1.4'),
    [string]$ZipPath = (Join-Path $PSScriptRoot 'artifacts\DuplicateChecker-1.4-windows-x64.zip'),
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$ZipPath=[IO.Path]::GetFullPath($ZipPath)
New-Item -ItemType Directory -Path $OutputDirectory,(Split-Path -Parent $ZipPath) -Force | Out-Null
if(@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count){throw 'The standalone publish directory must be empty.'}
$publish=@('publish',(Join-Path $PSScriptRoot 'App\DuplicateChecker.csproj'),'-c','Release','-r','win-x64','-p:Platform=x64','--self-contained','true','-p:PublishReadyToRun=false','-p:NuGetAudit=false','-p:IncludeSourceRevisionInInformationalVersion=false','-o',$OutputDirectory)
if($NoRestore){$publish+='--no-restore'}
& dotnet @publish
if($LASTEXITCODE -ne 0){throw 'Duplicate Checker publish failed'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $OutputDirectory
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $ZipPath){Remove-Item -LiteralPath $ZipPath -Force}
[IO.Compression.ZipFile]::CreateFromDirectory($OutputDirectory,$ZipPath,[IO.Compression.CompressionLevel]::Optimal,$false)
$checksum=(Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText($ZipPath+'.sha256',"$checksum  $(Split-Path -Leaf $ZipPath)`r`n",[Text.Encoding]::ASCII)
Write-Output ('Standalone archive: '+$ZipPath)

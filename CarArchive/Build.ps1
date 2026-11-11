$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'CarArchive.csproj'
$outputDirectory = Join-Path $PSScriptRoot 'artifacts\win-x64'
dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishReadyToRun=false -p:PublishTrimmed=false -o $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'CarArchive publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputDirectory 'README.md')
Write-Output (Join-Path $outputDirectory 'CarArchive.exe')

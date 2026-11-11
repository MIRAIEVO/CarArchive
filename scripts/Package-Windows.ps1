$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseRoot = Join-Path $repositoryRoot 'artifacts\CarArchive-1.0'
$programDirectory = Join-Path $releaseRoot 'Program'
$documentationDirectory = Join-Path $releaseRoot 'Documentation'
$dataDirectory = Join-Path $releaseRoot 'Data-and-Backup'
$archivePath = Join-Path $repositoryRoot 'artifacts\CarArchive-1.0-windows-x64.zip'
$project = Join-Path $repositoryRoot 'CarArchive\CarArchive.csproj'

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $programDirectory,$documentationDirectory,$dataDirectory -Force | Out-Null

dotnet publish $project -c Release -r win-x64 -p:Platform=x64 -p:PublishReadyToRun=false -p:PublishTrimmed=false -o $programDirectory
if ($LASTEXITCODE -ne 0) { throw 'CarArchive publish failed.' }

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CarArchive\README.md') -Destination (Join-Path $documentationDirectory 'README.md')
$versionNotes = @'
CarArchive 1.0

Windows x64 self-contained version. Extract the complete archive and launch CarArchive
using the shortcut in the package root. Keep all files inside the Program directory.
'@
Set-Content -LiteralPath (Join-Path $documentationDirectory 'Release-Notes.txt') -Value $versionNotes -Encoding UTF8
$dataNotes = @'
Application data is stored in %LOCALAPPDATA%\CarArchive for the current Windows user,
not in this release directory. Close the app before manually copying this data directory.

The Gemini API key is encrypted with Windows DPAPI for the current user. Database and
cache data can be backed up; the encrypted credential cannot be moved to another user.
'@
Set-Content -LiteralPath (Join-Path $dataDirectory 'Backup-Notes.txt') -Value $dataNotes -Encoding UTF8

$shortcutSource = @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
public class CarArchiveShellLinkClass {}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICarArchiveShellLink
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, IntPtr findData, uint flags);
    void GetIDList(out IntPtr idList);
    void SetIDList(IntPtr idList);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int max);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int max);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCommand);
    void SetShowCmd(int showCommand);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int max, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
    void Resolve(IntPtr window, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

public static class CarArchiveShortcut
{
    public static void Create(string shortcutPath, string targetPath, string relativeTarget, string workingDirectory)
    {
        var link = (ICarArchiveShellLink)new CarArchiveShellLinkClass();
        link.SetPath(targetPath);
        link.SetRelativePath(relativeTarget, 0);
        link.SetWorkingDirectory(workingDirectory);
        link.SetDescription("Launch CarArchive");
        link.SetIconLocation(targetPath, 0);
        link.SetShowCmd(1);
        ((IPersistFile)link).Save(shortcutPath, true);
    }

    public static string Resolve(string shortcutPath)
    {
        var link = (ICarArchiveShellLink)new CarArchiveShellLinkClass();
        ((IPersistFile)link).Load(shortcutPath, 0);
        link.Resolve(IntPtr.Zero, 1);
        var path = new StringBuilder(1024);
        link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
        return path.ToString();
    }
}
'@
Add-Type -TypeDefinition $shortcutSource

$shortcutPath = Join-Path $releaseRoot 'CarArchive.lnk'
$targetPath = Join-Path $programDirectory 'CarArchive.exe'
[CarArchiveShortcut]::Create($shortcutPath,$targetPath,'Program\CarArchive.exe',$programDirectory)
$resolvedTarget = [CarArchiveShortcut]::Resolve($shortcutPath)
if ($resolvedTarget -ne $targetPath -or -not (Test-Path -LiteralPath $resolvedTarget)) {
    throw "The launch shortcut does not resolve to the published app: $resolvedTarget"
}
$movedPackage = Join-Path $env:TEMP ('CarArchive-Shortcut-Test-' + [guid]::NewGuid().ToString('N'))
try {
    Move-Item -LiteralPath $releaseRoot -Destination $movedPackage
    $movedTarget = Join-Path $movedPackage 'Program\CarArchive.exe'
    $movedShortcut = Join-Path $movedPackage 'CarArchive.lnk'
    $movedResolvedTarget = [CarArchiveShortcut]::Resolve($movedShortcut)
    if ($movedResolvedTarget -ne $movedTarget) {
        throw "The launch shortcut is not portable after extraction: $movedResolvedTarget"
    }
}
finally {
    if (Test-Path -LiteralPath $movedPackage) { Move-Item -LiteralPath $movedPackage -Destination $releaseRoot }
}

if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
Write-Output "Package: $archivePath"

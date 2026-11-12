$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot '..\scripts\Package-Portable.ps1') @args

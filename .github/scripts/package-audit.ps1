#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Inspects every packed .nupkg so no secret material or unexpected file ships to nuget.org.

.DESCRIPTION
    A .nupkg is a zip. For each package in -PackageDir this extracts it, prints its full file list (so
    a reviewer can see what ships), then fails if it contains a denylisted file type (keys,
    certificates, dotenv, settings files) or a high-signal secret pattern inside a shipped text file,
    the backoffice client's JavaScript included. Symbol packages (.snupkg) hold only symbols and are
    not inspected. Run `dotnet pack` first.

    Deliberately dependency-free (no external scanner) so the check is deterministic and runs the
    same in CI and locally.

.PARAMETER PackageDir
    Directory holding the packed .nupkg files. Defaults to ./artifacts.

.OUTPUTS
    Exit code 0 when every package is clean, 1 otherwise.
#>
[CmdletBinding()]
param(
    [string]$PackageDir = './artifacts'
)

$ErrorActionPreference = 'Stop'

# Denylisted file names and types, and secret patterns to look for in shipped text files.
$denyGlobs = '*.pfx', '*.snk', '*.p12', '*.pem', '*.key', '.env', 'config.json', 'appsettings*.json'
$patterns = 'client_secret', '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----', 'xoxb-[0-9A-Za-z-]+', 'AKIA[0-9A-Z]{16}'
$textExt = '.json', '.xml', '.txt', '.md', '.nuspec', '.props', '.targets', '.config', '.ps1', '.js', '.mjs', '.map', '.css', '.html'

$packages = @(Get-ChildItem -Path $PackageDir -Filter *.nupkg -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0) {
    Write-Host "::error::no .nupkg found in $PackageDir"
    exit 1
}

$failed = $false
foreach ($nupkg in $packages) {
    $dest = Join-Path ([System.IO.Path]::GetTempPath()) ("pkgaudit-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $dest | Out-Null

    # Expand-Archive requires a .zip extension, so copy the nupkg to a .zip name first.
    $zipCopy = Join-Path $dest ($nupkg.BaseName + '.zip')
    Copy-Item -Path $nupkg.FullName -Destination $zipCopy
    Expand-Archive -Path $zipCopy -DestinationPath $dest -Force
    Remove-Item $zipCopy

    Write-Host "Package contents ($($nupkg.Name)):"
    Get-ChildItem -Recurse -File $dest |
        ForEach-Object { $_.FullName.Substring($dest.Length + 1) } |
        Sort-Object |
        ForEach-Object { Write-Host "  $_" }

    # 1) Denylisted file types that should never ship in a package.
    $bad = foreach ($glob in $denyGlobs) {
        Get-ChildItem -Recurse -File -Path $dest -Filter $glob -ErrorAction SilentlyContinue
    }
    if ($bad) {
        Write-Host "::error::denylisted or secret-bearing file(s) in $($nupkg.Name):"
        $bad | ForEach-Object { Write-Host "  $($_.FullName.Substring($dest.Length + 1))" }
        $failed = $true
    }

    # 2) High-signal secret patterns inside shipped text files.
    $hits = Get-ChildItem -Recurse -File -Path $dest |
        Where-Object { $textExt -contains $_.Extension.ToLower() } |
        Select-String -Pattern $patterns -List -ErrorAction SilentlyContinue
    if ($hits) {
        Write-Host "::error::secret-like pattern in $($nupkg.Name):"
        $hits | ForEach-Object { Write-Host "  $($_.Path.Substring($dest.Length + 1)):$($_.LineNumber)" }
        $failed = $true
    }

    Remove-Item -Recurse -Force $dest
}

if ($failed) {
    exit 1
}

Write-Host "PASS: $($packages.Count) package(s) contain no denylisted files or secret-like patterns."
exit 0

<#
.SYNOPSIS
    Lays out the exact NSIS + nsis_tauri_utils toolset the Tauri bundler ships, so the
    host-lock harnesses compile against the same compiler and plugin as a real release.

.DESCRIPTION
    Tauri's bundler downloads NSIS 3.11 (makensis.exe at the root of the zip) and
    nsis_tauri_utils 0.5.3 (a single DLL, pinned by SHA-1) on demand. Testing against a
    different pair proves nothing about the artifact that actually ships, so both are
    pinned here rather than resolved to "latest".

    Idempotent: the CI job caches $Destination across runs, so this only downloads again
    when makensis.exe is missing or the plugin's hash does not match.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Destination
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$NsisZipUrl = 'https://github.com/tauri-apps/binary-releases/releases/download/nsis-3.11/nsis-3.11.zip'
$PluginUrl = 'https://github.com/tauri-apps/nsis-tauri-utils/releases/download/nsis_tauri_utils-v0.5.3/nsis_tauri_utils.dll'
$PluginSha1 = '75197FEE3C6A814FE035788D1C34EAD39349B860'

$makensisPath = Join-Path $Destination 'makensis.exe'
$pluginDir = Join-Path $Destination 'Plugins\x86-unicode\additional'
$pluginPath = Join-Path $pluginDir 'nsis_tauri_utils.dll'

function Test-PluginHashMatches([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash
    return $actual.Equals($PluginSha1, [System.StringComparison]::OrdinalIgnoreCase)
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

if (Test-Path -LiteralPath $makensisPath) {
    Write-Host "makensis.exe already present at $makensisPath - skipping the NSIS download."
} else {
    Write-Host "Downloading NSIS 3.11 from $NsisZipUrl"
    $zipPath = Join-Path ([System.IO.Path]::GetTempPath()) "nsis-3.11-$([guid]::NewGuid()).zip"
    $extractPath = Join-Path ([System.IO.Path]::GetTempPath()) "nsis-3.11-$([guid]::NewGuid())"
    try {
        Invoke-WebRequest -Uri $NsisZipUrl -OutFile $zipPath -UseBasicParsing
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath -Force

        # The zip may contain makensis.exe at its root, or nest everything under a single
        # top-level folder (e.g. "nsis-3.11\"). Flatten either shape into $Destination.
        $rootMakensis = Get-ChildItem -LiteralPath $extractPath -Filter 'makensis.exe' -Recurse |
            Select-Object -First 1
        if (-not $rootMakensis) {
            throw "makensis.exe was not found anywhere inside $NsisZipUrl after extraction."
        }
        $sourceRoot = $rootMakensis.Directory.FullName

        Get-ChildItem -LiteralPath $sourceRoot | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
        }
    } finally {
        Remove-Item -LiteralPath $zipPath -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $extractPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path -LiteralPath $makensisPath)) {
        throw "makensis.exe still missing at $makensisPath after extracting $NsisZipUrl."
    }
    Write-Host "makensis.exe laid out at $makensisPath"
}

if (Test-PluginHashMatches $pluginPath) {
    Write-Host "nsis_tauri_utils.dll already present at $pluginPath with the expected SHA-1 - skipping the download."
} else {
    Write-Host "Downloading nsis_tauri_utils.dll from $PluginUrl"
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    $tempPluginPath = Join-Path ([System.IO.Path]::GetTempPath()) "nsis_tauri_utils-$([guid]::NewGuid()).dll"
    try {
        Invoke-WebRequest -Uri $PluginUrl -OutFile $tempPluginPath -UseBasicParsing
        $actualHash = (Get-FileHash -LiteralPath $tempPluginPath -Algorithm SHA1).Hash
        if (-not $actualHash.Equals($PluginSha1, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "nsis_tauri_utils.dll SHA-1 mismatch: expected $PluginSha1, got $actualHash. " +
                "Refusing to install a plugin build that was not pinned for this toolset."
        }
        Copy-Item -LiteralPath $tempPluginPath -Destination $pluginPath -Force
    } finally {
        Remove-Item -LiteralPath $tempPluginPath -ErrorAction SilentlyContinue
    }
    Write-Host "nsis_tauri_utils.dll laid out at $pluginPath"
}

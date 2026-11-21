# PowerShell counterpart of stage-dotnet-runtime.sh, same arguments, environment and checks.
# Usage: stage-dotnet-runtime.ps1 <rid> <dest>; the host channel is pinned to the building SDK's patch.
param(
	[Parameter(Mandatory = $true, Position = 0)][string]$Rid,
	[Parameter(Mandatory = $true, Position = 1)][string]$Dest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Fail([string[]]$Lines) {
	foreach ($line in $Lines) {
		[Console]::Error.WriteLine($line)
	}
	exit 1
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$cache = if ($env:MACRODECK_RUNTIME_CACHE) { $env:MACRODECK_RUNTIME_CACHE } else { Join-Path $root '.cache/dotnet-runtime' }

$sdkRuntimeVersion = $env:MACRODECK_SDK_RUNTIME_VERSION
if (-not $sdkRuntimeVersion) {
	$sdkRuntimeVersion = (& dotnet msbuild (Join-Path $root 'host/src/MacroDeckHost/MacroDeckHost.csproj') `
		-getProperty:BundledNETCoreAppPackageVersion | Out-String).Trim()
	if ($LASTEXITCODE -ne 0) {
		$sdkRuntimeVersion = ''
	}
}
if (-not $sdkRuntimeVersion) {
	Fail "error: could not determine the SDK's runtime version"
}

$archives = & node (Join-Path $root 'ci/scripts/resolve-dotnet-runtime.mjs') `
	(Join-Path $root 'ci/dotnet-runtime/bundled-runtimes.json') $sdkRuntimeVersion $Rid
if ($LASTEXITCODE -ne 0) {
	exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path $cache, $Dest | Out-Null
$Dest = (Resolve-Path $Dest).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $work | Out-Null

try {
	foreach ($line in @($archives)) {
		$version, $url, $sha512 = "$line".Split("`t")
		if (-not $version) {
			continue
		}
		$extension = if ($url.EndsWith('.zip')) {
			'zip'
		} elseif ($url.EndsWith('.tar.gz')) {
			'tar.gz'
		} else {
			Fail "error: unexpected runtime archive type: $url"
		}
		$archive = Join-Path $cache "$sha512.$extension"

		if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
			Write-Output "Downloading .NET runtime $version for $Rid"
			Invoke-WebRequest -Uri $url -OutFile "$archive.partial" -MaximumRetryCount 3 -RetryIntervalSec 2
			Move-Item -LiteralPath "$archive.partial" -Destination $archive -Force
		}
		$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant()
		if ($actual -ne $sha512.ToLowerInvariant()) {
			Remove-Item -LiteralPath $archive -Force
			Fail "error: sha512 mismatch for $url", "       expected $sha512", "       actual   $actual"
		}

		$extracted = Join-Path $work $version
		New-Item -ItemType Directory -Force -Path $extracted | Out-Null
		if ($extension -eq 'zip') {
			Add-Type -AssemblyName System.IO.Compression.FileSystem
			[System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $extracted, $true)
		} else {
			& tar -xzf $archive -C $extracted
			if ($LASTEXITCODE -ne 0) {
				Fail "error: could not extract $archive"
			}
		}
		foreach ($file in Get-ChildItem -LiteralPath $extracted -Recurse -File -Force) {
			$target = Join-Path $Dest ([System.IO.Path]::GetRelativePath($extracted, $file.FullName))
			New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
			Copy-Item -LiteralPath $file.FullName -Destination $target -Force
		}
		Write-Output "Staged .NET runtime $version"
	}
} finally {
	Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

# The LTTng trace provider links liblttng-ust.so.0, which Ubuntu 22.04 lacks, and breaks linuxdeploy.
Get-ChildItem -Path (Join-Path $Dest 'shared/Microsoft.NETCore.App/*/libcoreclrtraceptprovider.so') -ErrorAction SilentlyContinue |
	Remove-Item -Force

if (-not (Test-Path -LiteralPath (Join-Path $Dest 'dotnet') -PathType Leaf) `
	-and -not (Test-Path -LiteralPath (Join-Path $Dest 'dotnet.exe') -PathType Leaf)) {
	Fail "error: no dotnet muxer in $Dest"
}
foreach ($framework in 'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App') {
	if (-not (Test-Path -LiteralPath (Join-Path $Dest "shared/$framework/$sdkRuntimeVersion") -PathType Container)) {
		Fail "error: $Dest is missing $framework $sdkRuntimeVersion"
	}
}
Write-Output "Runtime staged in $Dest"

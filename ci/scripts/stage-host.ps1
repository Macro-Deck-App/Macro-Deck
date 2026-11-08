# PowerShell counterpart of stage-host.sh: stages the host publish output into ui/bootstrapper/host-publish.
# Usage: stage-host.ps1 <rid> [version] [build-channel]
param(
	[Parameter(Mandatory = $true, Position = 0)][string]$Rid,
	[Parameter(Position = 1)][string]$Version = '3.0.0-dev.0',
	[Parameter(Position = 2)][string]$BuildChannel = 'Development'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Warn([string[]]$Lines) {
	foreach ($line in $Lines) {
		[Console]::Error.WriteLine($line)
	}
}

function Touch([string]$Path) {
	if (Test-Path -LiteralPath $Path) {
		(Get-Item -LiteralPath $Path -Force).LastWriteTime = Get-Date
	} else {
		New-Item -ItemType File -Path $Path | Out-Null
	}
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$publishDir = Join-Path $root 'ui/bootstrapper/host-publish'

if (Test-Path -LiteralPath $publishDir) {
	Remove-Item -LiteralPath $publishDir -Recurse -Force
}
& dotnet publish (Join-Path $root 'host/src/MacroDeckHost') `
	-c Release `
	-r $Rid `
	"-p:Version=$Version" `
	"-p:BuildChannel=$BuildChannel" `
	-p:DebugType=None `
	-p:DebugSymbols=false `
	-o $publishDir
if ($LASTEXITCODE -ne 0) {
	exit $LASTEXITCODE
}
& (Join-Path $PSScriptRoot 'stage-dotnet-runtime.ps1') $Rid (Join-Path $publishDir 'runtime')
if ($LASTEXITCODE -ne 0) {
	exit $LASTEXITCODE
}

# wwwroot layout: web-client (public deck) at /, desktop-ui (configuration UI) at /admin, and one
# device-specific web-client build per target under /targets/<id>.
$desktopUiDist = Join-Path $root 'ui/angular/dist/desktop-ui/browser'
$webClientDist = Join-Path $root 'ui/web-client/dist'
$carthingDist = Join-Path $root 'ui/web-client/dist-carthing'
$wwwroot = Join-Path $publishDir 'wwwroot'
if (Test-Path -LiteralPath $wwwroot) {
	Remove-Item -LiteralPath $wwwroot -Recurse -Force
}
if (Test-Path -LiteralPath $webClientDist -PathType Container) {
	Copy-Item -LiteralPath $webClientDist -Destination $wwwroot -Recurse
} else {
	Warn "warning: web-client dist not found ($webClientDist), web client will be missing from wwwroot"
	New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
}
if (Test-Path -LiteralPath $desktopUiDist -PathType Container) {
	Copy-Item -LiteralPath $desktopUiDist -Destination (Join-Path $wwwroot 'admin') -Recurse
} else {
	Warn "warning: desktop-ui dist not found ($desktopUiDist), /admin will be missing from wwwroot"
}
if (Test-Path -LiteralPath $carthingDist -PathType Container) {
	New-Item -ItemType Directory -Force -Path (Join-Path $wwwroot 'targets') | Out-Null
	Copy-Item -LiteralPath $carthingDist -Destination (Join-Path $wwwroot 'targets/carthing') -Recurse
} else {
	Warn "warning: carthing dist not found ($carthingDist), /targets/carthing will be missing from wwwroot"
}

# The client is copied, never built, so a stale dist makes it report itself out of date on load:
# saying so here beats working that out from the symptom.
$stagedShell = Join-Path $wwwroot 'index.html'
if (Test-Path -LiteralPath $stagedShell -PathType Leaf) {
	$match = [regex]::Match((Get-Content -LiteralPath $stagedShell -Raw), 'macro-deck-ui-commit" content="([^"]*)"')
	$uiCommit = if ($match.Success) { $match.Groups[1].Value } else { '' }
	$hostCommit = ''
	if (Get-Command git -ErrorAction SilentlyContinue) {
		$hostCommit = (& git -C $root rev-parse --short=7 HEAD 2>$null | Out-String).Trim()
		if ($LASTEXITCODE -ne 0) {
			$hostCommit = ''
		}
	}
	if ($uiCommit -and $hostCommit -and $uiCommit -ne $hostCommit) {
		Warn "warning: the staged client is from $uiCommit but this host is $hostCommit;",
			"         run 'npm run build' in ui/web-client and stage again, or the client will",
			'         report itself out of date'
	}
}

Touch (Join-Path $publishDir '.macro-deck-packaged')
# Restore the tracked placeholder the removal above deleted (see .gitignore).
Touch (Join-Path $publishDir '.gitkeep')
Write-Output "Host staged in $publishDir"

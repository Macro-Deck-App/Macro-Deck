# The .NET host is a single-file executable whose managed assemblies are
# embedded. Sign exactly that launcher before Tauri packages host-publish/.
param(
	[Parameter(Mandatory = $true)]
	[string] $Directory
)

$ErrorActionPreference = 'Stop'

$hosts = @(Get-ChildItem -LiteralPath $Directory -File |
	Where-Object { $_.Name -match '^MacroDeckHost(?:Development)?\.exe$' })
if ($hosts.Count -ne 1) {
	throw "Expected exactly one Macro Deck host executable in ${Directory}; found $($hosts.Count)"
}

& (Join-Path $PSScriptRoot 'sign-windows-file.ps1') -FilePath $hosts[0].FullName
if ($LASTEXITCODE -ne 0) {
	exit $LASTEXITCODE
}

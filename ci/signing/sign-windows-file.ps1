# Signs one first-party Windows executable with SSL.com eSigner. Tauri also
# invokes this hook for five NSIS plugin DLLs; those are intentionally skipped
# because each remote signature consumes the monthly quota.
param(
	[Parameter(Mandatory = $true)]
	[string] $FilePath
)

$ErrorActionPreference = 'Stop'

if ($env:WINDOWS_SIGNING_ENABLED -ne 'true') {
	Write-Host "[sign] Windows signing disabled, skipping $FilePath"
	exit 0
}

$file = Get-Item -LiteralPath $FilePath
if ($file.Extension -ieq '.dll') {
	Write-Host "[sign] Quota guard: skipping NSIS DLL $($file.Name)"
	exit 0
}
if ($file.Extension -ine '.exe') {
	throw "Refusing to spend an eSigner signature on unexpected file type: $($file.FullName)"
}

$required = @(
	'CODE_SIGN_TOOL_PATH',
	'ES_USERNAME',
	'ES_PASSWORD',
	'ES_CREDENTIAL_ID',
	'ES_TOTP_SECRET'
)
$missing = $required | Where-Object { -not [Environment]::GetEnvironmentVariable($_) }
if ($missing.Count -gt 0) {
	throw "Missing eSigner configuration: $($missing -join ', ')"
}

$tool = Join-Path $env:CODE_SIGN_TOOL_PATH 'CodeSignTool.bat'
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
	throw "SSL.com CodeSignTool was not found at $tool"
}

$manifest = Join-Path $env:RUNNER_TEMP 'macro-deck-esigner-signatures.txt'
$completedRoles = if (Test-Path -LiteralPath $manifest) {
	@(Get-Content -LiteralPath $manifest | ForEach-Object { ($_ -split '\|', 2)[0] })
} else {
	@()
}

$name = $file.Name
$role = if ($name -match '^MacroDeckHost(?:Development)?\.exe$') {
	'Host'
} elseif ($name -eq 'MacroDeck.exe') {
	'App'
} elseif ($name -match '-setup\.exe$') {
	'Installer'
} else {
	# NSIS supplies a temporary path through !uninstfinalize; its generated
	# filename is an implementation detail and is not the installed uninstall.exe.
	# Accept that opaque name only in the exact position Tauri invokes it. This
	# prevents an unexpected executable from silently spending another signature.
	if (($completedRoles -join ',') -ne 'Host,App') {
		throw "Refusing to spend an eSigner signature on unexpected executable: $($file.FullName)"
	}
	'Uninstaller'
}

$expectedPreviousRoles = switch ($role) {
	'Host' { @() }
	'App' { @('Host') }
	'Uninstaller' { @('Host', 'App') }
	'Installer' { @('Host', 'App', 'Uninstaller') }
}
if (($completedRoles -join ',') -ne ($expectedPreviousRoles -join ',')) {
	throw "Refusing out-of-order or duplicate $role signature; completed roles: $($completedRoles -join ', ')"
}

Write-Host "[sign] Signing $role executable: $($file.FullName)"
& $tool sign `
	"-username=$env:ES_USERNAME" `
	"-password=$env:ES_PASSWORD" `
	"-credential_id=$env:ES_CREDENTIAL_ID" `
	"-totp_secret=$env:ES_TOTP_SECRET" `
	"-input_file_path=$($file.FullName)" `
	'-override=true' `
	'-malware_block=false'
if ($LASTEXITCODE -ne 0) {
	throw "eSigner failed for $($file.FullName) with exit code $LASTEXITCODE"
}

$signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
if ($signature.Status -ne 'Valid') {
	throw "Invalid Authenticode signature for $($file.FullName): $($signature.Status) $($signature.StatusMessage)"
}
if (-not $signature.TimeStamperCertificate) {
	throw "The Authenticode signature for $($file.FullName) has no timestamp"
}

Add-Content -LiteralPath $manifest -Value "$role|$($file.FullName)"
Write-Host "[sign] $role signature is valid and timestamped"

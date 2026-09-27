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
if ($file.Extension -notin '.exe', '.tmp') {
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

# CodeSignTool.bat forwards %* through cmd.exe, which splits a secret at & | < > ^.
# Run its bundled Java and jar directly so each argument reaches the tool intact.
$java = Get-ChildItem -Path (Join-Path $env:CODE_SIGN_TOOL_PATH 'jdk-*\bin\java.exe') -File | Select-Object -First 1
$jar = Get-ChildItem -Path (Join-Path $env:CODE_SIGN_TOOL_PATH 'jar\code_sign_tool-*.jar') -File | Select-Object -First 1
if (-not $java -or -not $jar) {
	throw "SSL.com CodeSignTool was not found under $env:CODE_SIGN_TOOL_PATH"
}

$manifest = Join-Path $env:RUNNER_TEMP 'macro-deck-esigner-signatures.txt'
$completedRoles = if (Test-Path -LiteralPath $manifest) {
	@(Get-Content -LiteralPath $manifest | ForEach-Object { ($_ -split '\|', 2)[0] })
} else {
	@()
}

$name = $file.Name
$role = if ($file.Extension -ieq '.tmp') {
	# NSIS hands the uninstaller to !uninstfinalize as an opaque nst*.tmp file. Accept
	# it only as a PE file in the exact position Tauri signs the uninstaller.
	$header = [byte[]]::new(2)
	$stream = [System.IO.File]::OpenRead($file.FullName)
	try { $null = $stream.Read($header, 0, 2) } finally { $stream.Dispose() }
	if (($completedRoles -join ',') -ne 'Host,App' -or [System.Text.Encoding]::ASCII.GetString($header) -ne 'MZ') {
		throw "Refusing to spend an eSigner signature on unexpected file: $($file.FullName)"
	}
	'Uninstaller'
} elseif ($name -match '^MacroDeckHost(?:Development)?\.exe$') {
	'Host'
} elseif ($name -eq 'MacroDeck.exe') {
	'App'
} elseif ($name -match '-setup\.exe$') {
	'Installer'
} else {
	throw "Refusing to spend an eSigner signature on unexpected executable: $($file.FullName)"
}

# Tauri re-signs any bundled executable that signtool finds unsigned. A dry run leaves
# the host unsigned, so that second call is one a signed run never makes.
if ($env:ESIGNER_DRY_RUN -eq 'true' -and $role -eq 'Host' -and $completedRoles -contains 'Host') {
	Write-Host "[sign] Dry run: skipping Tauri's re-sign of the unsigned host; a signed run skips it too"
	exit 0
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

# CodeSignTool picks the signature format from the file extension, so a .tmp is
# signed as an .exe copy that replaces the original once it verifies.
$signPath = $file.FullName
if ($file.Extension -ieq '.tmp') {
	$signPath = Join-Path $env:RUNNER_TEMP "macro-deck-$($role.ToLowerInvariant())-$([guid]::NewGuid().ToString('N')).exe"
	Copy-Item -LiteralPath $file.FullName -Destination $signPath
}

# A dry run authenticates with the same arguments through the same launcher but
# only reads the credential, so it proves the setup without spending quota.
if ($env:ESIGNER_DRY_RUN -eq 'true') {
	Write-Host "[sign] Dry run: checking eSigner credential instead of signing $role executable: $($file.FullName)"
	$output = @(& $java.FullName -Xmx1024M -jar $jar.FullName credential_info `
		"-username=$env:ES_USERNAME" `
		"-password=$env:ES_PASSWORD" `
		"-credential_id=$env:ES_CREDENTIAL_ID" 2>&1 | ForEach-Object { "$_" })
	$output | ForEach-Object { Write-Host $_ }
	if ($LASTEXITCODE -ne 0 -or -not ($output -match '^- Certificate Expiry: ')) {
		throw "eSigner credential check failed for $role with exit code $LASTEXITCODE"
	}
	if ($signPath -ne $file.FullName) {
		Copy-Item -LiteralPath $signPath -Destination $file.FullName -Force
		Remove-Item -LiteralPath $signPath
	}
	Add-Content -LiteralPath $manifest -Value "$role|$($file.FullName)"
	Write-Host "[sign] Dry run: $role would spend one eSigner signature"
	exit 0
}

Write-Host "[sign] Signing $role executable: $($file.FullName)"
& $java.FullName -Xmx1024M -jar $jar.FullName sign `
	"-username=$env:ES_USERNAME" `
	"-password=$env:ES_PASSWORD" `
	"-credential_id=$env:ES_CREDENTIAL_ID" `
	"-totp_secret=$env:ES_TOTP_SECRET" `
	"-input_file_path=$signPath" `
	'-override=true' `
	'-malware_block=false'
if ($LASTEXITCODE -ne 0) {
	throw "eSigner failed for $($file.FullName) with exit code $LASTEXITCODE"
}

$signature = Get-AuthenticodeSignature -LiteralPath $signPath
if ($signature.Status -ne 'Valid') {
	throw "Invalid Authenticode signature for $($file.FullName): $($signature.Status) $($signature.StatusMessage)"
}
if (-not $signature.TimeStamperCertificate) {
	throw "The Authenticode signature for $($file.FullName) has no timestamp"
}
if ($signPath -ne $file.FullName) {
	Copy-Item -LiteralPath $signPath -Destination $file.FullName -Force
	Remove-Item -LiteralPath $signPath
}

Add-Content -LiteralPath $manifest -Value "$role|$($file.FullName)"
Write-Host "[sign] $role signature is valid and timestamped"

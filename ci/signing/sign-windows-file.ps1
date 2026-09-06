# Signs a single binary. Wired as the Tauri bundler's windows.signCommand so
# the bootstrapper executable and the NSIS installer get Authenticode
# signatures. Uses the same environment variables as sign-windows-files.ps1:
#   WINDOWS_SIGN_CERT_PFX_FILE, WINDOWS_SIGN_CERT_PASSWORD
# Exits successfully without signing when no certificate is configured, so
# unsigned CI runs and local packaging keep working (mirrors the HAS_WIN_CERT
# gate of the release workflow).
param(
	[Parameter(Mandatory = $true)]
	[string] $FilePath
)

$ErrorActionPreference = 'Stop'

if (-not $env:WINDOWS_SIGN_CERT_PFX_FILE) {
	Write-Host "[sign] WINDOWS_SIGN_CERT_PFX_FILE not set, skipping $FilePath"
	exit 0
}

$password = if ($env:WINDOWS_SIGN_CERT_PASSWORD) {
	ConvertTo-SecureString -String $env:WINDOWS_SIGN_CERT_PASSWORD -AsPlainText -Force
} else {
	New-Object System.Security.SecureString
}
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
	$env:WINDOWS_SIGN_CERT_PFX_FILE, $password)

$result = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $cert `
	-HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
# UnknownError is what a self-signed cert without a trusted chain reports;
# accepted until the ssl.com certificate replaces it (see engineering/development/releasing.md).
if ($result.Status -ne 'Valid' -and $result.Status -ne 'UnknownError') {
	throw "Signing failed for ${FilePath}: $($result.Status) $($result.StatusMessage)"
}
Write-Host "[sign] ${FilePath}: $($result.Status)"

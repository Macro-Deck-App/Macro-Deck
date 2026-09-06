# Signs the .NET host binaries before the Tauri bundler packs them as
# resources. Uses the same environment variables as sign-windows-file.ps1:
#   WINDOWS_SIGN_CERT_PFX_FILE, WINDOWS_SIGN_CERT_PASSWORD
param(
	[Parameter(Mandatory = $true)]
	[string] $Directory
)

$ErrorActionPreference = 'Stop'

if (-not $env:WINDOWS_SIGN_CERT_PFX_FILE) {
	Write-Host '[sign] WINDOWS_SIGN_CERT_PFX_FILE not set, skipping host signing'
	exit 0
}

$password = if ($env:WINDOWS_SIGN_CERT_PASSWORD) {
	ConvertTo-SecureString -String $env:WINDOWS_SIGN_CERT_PASSWORD -AsPlainText -Force
} else {
	New-Object System.Security.SecureString
}
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
	$env:WINDOWS_SIGN_CERT_PFX_FILE, $password)

# Matches exactly the single-file host launcher (MacroDeckHost.exe or
# MacroDeckHostDevelopment.exe); every other assembly is bundled inside it.
$files = Get-ChildItem -Path $Directory -File |
	Where-Object { $_.Name -match '^(MacroDeckHost.*\.(exe|dll)|MacroDeck\.Sdk\.dll)$' }

foreach ($file in $files) {
	$result = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert `
		-HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
	if ($result.Status -ne 'Valid' -and $result.Status -ne 'UnknownError') {
		throw "Signing failed for $($file.Name): $($result.Status) $($result.StatusMessage)"
	}
	Write-Host "[sign] $($file.Name): $($result.Status)"
}

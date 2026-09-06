# One-time helper: creates a self-signed code-signing certificate and prints
# the two GitHub secrets needed by the pipeline. Run on any Windows machine:
#   powershell -ExecutionPolicy Bypass -File ci/signing/create-selfsigned-cert.ps1
# Replaced by the ssl.com certificate once it arrives (see docs/RELEASING.md).
param(
	[string] $Subject = 'CN=Macro Deck (self-signed)',
	[int] $ValidYears = 3
)

$ErrorActionPreference = 'Stop'

$password = [System.Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
$securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force

$cert = New-SelfSignedCertificate `
	-Type CodeSigningCert `
	-Subject $Subject `
	-KeyAlgorithm RSA `
	-KeyLength 4096 `
	-HashAlgorithm SHA256 `
	-NotAfter (Get-Date).AddYears($ValidYears) `
	-CertStoreLocation 'Cert:\CurrentUser\My'

$pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) 'macro-deck-codesign.pfx'
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Remove-Item -Path ("Cert:\CurrentUser\My\" + $cert.Thumbprint)

$pfxBase64 = [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($pfxPath))
Remove-Item -Path $pfxPath

Write-Host 'Add these GitHub Actions secrets:'
Write-Host ''
Write-Host 'WINDOWS_SIGN_CERT_PFX_BASE64:'
Write-Host $pfxBase64
Write-Host ''
Write-Host 'WINDOWS_SIGN_CERT_PASSWORD:'
Write-Host $password

$ErrorActionPreference = 'Stop'

$download = Join-Path $env:RUNNER_TEMP 'codesigntool-download'
$toolPath = Join-Path $env:RUNNER_TEMP 'codesign\CodeSignTool-v1.3.0'
$zip = Join-Path $download 'CodeSignTool-v1.3.0-windows.zip'

node (Join-Path $PSScriptRoot '..\scripts\pinned-tools.mjs') fetch codesigntool-windows $download
if ($LASTEXITCODE -ne 0) {
	throw "Fetching the pinned CodeSignTool failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $toolPath | Out-Null
# Windows' own bsdtar reads zip archives; a tar earlier on PATH (Git's GNU tar) does not.
& (Join-Path $env:SystemRoot 'System32\tar.exe') -xf $zip -C $toolPath
if ($LASTEXITCODE -ne 0) {
	throw "Extracting $zip failed with exit code $LASTEXITCODE"
}

$java = Get-ChildItem -Path (Join-Path $toolPath 'jdk-*\bin\java.exe') -File | Select-Object -First 1
$jar = Get-ChildItem -Path (Join-Path $toolPath 'jar\code_sign_tool-*.jar') -File | Select-Object -First 1
if (-not $java -or -not $jar) {
	throw "The CodeSignTool archive does not contain the expected jdk-* and jar folders under $toolPath"
}

Set-Content -LiteralPath (Join-Path $toolPath 'conf\code_sign_tool.properties') -Encoding utf8NoBOM -Value @(
	'CLIENT_ID=kaXTRACNijSWsFdRKg_KAfD3fqrBlzMbWs6TwWHwAn8'
	'OAUTH2_ENDPOINT=https://login.ssl.com/oauth2/token'
	'CSC_API_ENDPOINT=https://cs.ssl.com'
	'TSA_URL=http://ts.ssl.com'
	'TSA_LEGACY_URL=http://ts.ssl.com/legacy'
)

"CODE_SIGN_TOOL_PATH=$toolPath" >> $env:GITHUB_ENV
Write-Host "CodeSignTool v1.3.0 verified and installed at $toolPath"

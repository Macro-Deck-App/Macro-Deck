<#
.SYNOPSIS
    Compiles and runs the installer/tests/*.nsi harnesses against real Windows state to prove
    installer/host-lock.nsh, installer/hooks.nsh and installer/firewall.nsh do what their
    regression guards claim - stopping a locked Macro Deck host (issue #131) and creating/removing
    the inbound Windows Firewall rule (issue #345) - and reports a pass/fail/skip table for CI.

.DESCRIPTION
    Everything here needs the pinned NSIS 3.11 + nsis_tauri_utils 0.5.3 toolset laid out by
    Get-NsisToolset.ps1 at -NsisRoot (Windows-only: nsis_tauri_utils is a Windows plugin).
    firewall-harness.nsi does not use nsis_tauri_utils, but is compiled by the same toolset for
    convenience.

    The firewall scenarios split into two groups. The command-shape scenarios (quoting, clause
    order, no localport, & vs &&) never touch a real firewall rule, so they always run. The
    scenarios that actually create/inspect/remove a rule with the NetSecurity module need an
    elevated shell and are wrapped with -SkipWhen (-not (Test-Elevated)) - a GitHub-hosted
    Windows runner's job actor is an administrator, so they run in CI, but a local, unprivileged
    invocation degrades to Skip instead of failing outright.

    Not exercised by anything below, on purpose:
      - A genuinely refused kill (host-lock.nsh's result=3 / MACRODECK_STOP_KILL_REFUSED)
        cannot be produced on a GitHub-hosted runner: the job's actor is an administrator, and
        KillProcessCurrentUser silently skips a process it cannot open rather than failing to
        terminate it. That path is covered only by hooks.nsh/host-lock.nsh compiling with the
        refusal branch reachable (installer_config.rs) and manual review, not by a scenario here.
      - Upgrading from a real, signed, published beta release remains a manual release check;
        InPlaceUpgrade below only approximates the shape of a real upgrade with unsigned,
        locally-built artifacts.
      - A declined UAC prompt (firewall.nsh's result=1 / MACRODECK_FIREWALL_LAUNCH_FAILED) cannot
        be produced here either: nothing in this driver can click "No" on a real elevation
        dialog, and there is no reliable, non-fake way to make ShellExecuteEx itself fail (a
        missing cmd.exe would not exercise the documented failure mode - a declined or blocked
        elevation). That path is covered only by firewall.nsh's own review and the fact that it
        compiles with the branch reachable, not by a scenario here.
      - What the elevated UAC dialog looks like, and its exact wording, is not asserted anywhere:
        MessageBox content is not something a headless CI run can observe short of OCR, and the
        wording itself is not the property under test.

.PARAMETER NsisRoot
    Directory produced by Get-NsisToolset.ps1: makensis.exe at its root, the plugin at
    Plugins\x86-unicode\additional\nsis_tauri_utils.dll underneath.

.PARAMETER WorkRoot
    Scratch directory for compiled harnesses, per-scenario install/probe directories, and logs.
    Must not contain spaces - NSIS's /D and -D command-line handling of quoted paths is brittle,
    and CI runner temp paths (e.g. D:\a\_temp) already avoid them.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$NsisRoot,

    [Parameter(Mandatory)]
    [string]$WorkRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-Dir([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path -LiteralPath $Path).Path
}

$script:TestsDir = $PSScriptRoot
$script:MakensisExe = Join-Path $NsisRoot 'makensis.exe'
$script:PluginsDir = Join-Path $NsisRoot 'Plugins\x86-unicode\additional'
$script:PowerShellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

if (-not (Test-Path -LiteralPath $script:MakensisExe)) {
    throw "makensis.exe not found at $script:MakensisExe - run Get-NsisToolset.ps1 first."
}
if (-not (Test-Path -LiteralPath $script:PowerShellExe)) {
    throw "$script:PowerShellExe not found - the fake host processes need a real, signable executable."
}

$script:BinDir = New-Dir (Join-Path $WorkRoot 'bin')
$script:ScenariosDir = New-Dir (Join-Path $WorkRoot 'scenarios')
$script:LogsDir = New-Dir (Join-Path $WorkRoot 'logs')

$script:HooksHarnessExe = Join-Path $script:BinDir 'hooks-harness.exe'
$script:HostLockHarnessNormalExe = Join-Path $script:BinDir 'host-lock-harness-normal.exe'
$script:HostLockHarnessShortExe = Join-Path $script:BinDir 'host-lock-harness-short.exe'
$script:MiniUpgradeV1Exe = Join-Path $script:BinDir 'mini-upgrade-v1.exe'
$script:MiniUpgradeV2Exe = Join-Path $script:BinDir 'mini-upgrade-v2.exe'
$script:FirewallHarnessExe = Join-Path $script:BinDir 'firewall-harness.exe'
$script:FirewallHarnessOpenExe = Join-Path $script:BinDir 'firewall-harness-open.exe'

$script:NormalTimeoutMs = 6000
$script:NormalPollMs = 100
$script:ShortTimeoutMs = 1500
$script:ShortPollMs = 100

$script:Results = [System.Collections.Generic.List[object]]::new()
$script:CompileStatus = @{}

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Field([hashtable]$Fields, [string]$Key, [string]$Expected) {
    Assert ($Fields.ContainsKey($Key)) "log is missing the '$Key' field"
    Assert ($Fields[$Key] -eq $Expected) "expected $Key=$Expected but got $Key=$($Fields[$Key])"
}

function Assert-OuterQuotePair([string]$Command) {
    $marker = '/D /S /C '
    $start = $Command.IndexOf($marker)
    Assert ($start -ge 0) "command does not contain '$marker': $Command"
    $body = $Command.Substring($start + $marker.Length)
    Assert ($body.StartsWith('"')) "the character after '/C ' must be the opening quote: $Command"
    Assert ($body.EndsWith('"')) "the command must end with the closing quote: $Command"
}

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Makensis {
    param(
        [Parameter(Mandatory)][string]$Script,
        [hashtable]$Defines = @{},
        [string[]]$ExtraArgs = @()
    )
    $defineArgs = foreach ($key in $Defines.Keys) { "/D$key=$($Defines[$key])" }
    $arguments = @('/V2') + $defineArgs + $ExtraArgs + @($Script)
    $output = & $script:MakensisExe @arguments 2>&1 | Out-String
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = $output
    }
}

function Invoke-CompileStep {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Body
    )
    Write-Host "==> Compile: $Name"
    try {
        & $Body
        Write-Host "    OK"
        $script:CompileStatus[$Name] = $true
        $script:Results.Add([pscustomobject]@{ Name = "Compile: $Name"; Status = 'Pass'; Detail = '' })
    } catch {
        Write-Host "    FAILED: $($_.Exception.Message)"
        $script:CompileStatus[$Name] = $false
        $script:Results.Add([pscustomobject]@{ Name = "Compile: $Name"; Status = 'Fail'; Detail = $_.Exception.Message })
    }
}

function Invoke-Scenario {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Body,
        [bool]$SkipWhen = $false,
        [string]$SkipReason = ''
    )
    Write-Host "==> $Name"
    if ($SkipWhen) {
        Write-Host "    SKIP: $SkipReason"
        $script:Results.Add([pscustomobject]@{ Name = $Name; Status = 'Skip'; Detail = $SkipReason })
        return
    }
    try {
        & $Body
        Write-Host "    PASS"
        $script:Results.Add([pscustomobject]@{ Name = $Name; Status = 'Pass'; Detail = '' })
    } catch {
        Write-Host "    FAIL: $($_.Exception.Message)"
        $script:Results.Add([pscustomobject]@{ Name = $Name; Status = 'Fail'; Detail = $_.Exception.Message })
    }
}

function New-ScenarioDir([string]$Name) {
    $dir = Join-Path $script:ScenariosDir $Name
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Start-FakeHostProcess {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ExeName
    )
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $target = Join-Path $Directory $ExeName
    Copy-Item -LiteralPath $script:PowerShellExe -Destination $target -Force
    Start-Process -FilePath $target `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 300') `
        -PassThru -WindowStyle Hidden
}

function Start-FileHolder {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$HoldSeconds
    )
    $escapedPath = $Path -replace "'", "''"
    $command = "`$fs = [System.IO.File]::Open('$escapedPath', [System.IO.FileMode]::OpenOrCreate, " +
        "[System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read); " +
        "Start-Sleep -Seconds $HoldSeconds; `$fs.Close()"
    Start-Process -FilePath $script:PowerShellExe `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', $command) `
        -PassThru -WindowStyle Hidden
}

function Wait-UntilLocked {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$TimeoutSeconds = 20
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $stream = [System.IO.File]::Open(
                $Path, [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $stream.Close()
        } catch {
            return
        }
        Start-Sleep -Milliseconds 50
    }
    throw "timed out waiting for $Path to be locked by the holder process"
}

function Stop-ProcessSafely($Process) {
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    } catch {
    }
}

function Invoke-Installer {
    param(
        [Parameter(Mandatory)][string]$Installer,
        [Parameter(Mandatory)][string]$InstallDir
    )
    return Start-Process -FilePath $Installer -ArgumentList @('/S', "/D=$InstallDir") `
        -Wait -PassThru -WindowStyle Hidden
}

function Invoke-HostLockHarness {
    param(
        [Parameter(Mandatory)][string]$HarnessExe,
        [Parameter(Mandatory)][string]$ProcessName,
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][string]$LogPath
    )
    if (Test-Path -LiteralPath $LogPath) {
        Remove-Item -LiteralPath $LogPath -Force
    }
    $env:MDTEST_PROCESS = $ProcessName
    $env:MDTEST_DIR = $Directory
    $env:MDTEST_LOG = $LogPath
    $env:MDTEST_SCOPE = $Scope
    $proc = Start-Process -FilePath $HarnessExe -PassThru -Wait -WindowStyle Hidden
    Assert (Test-Path -LiteralPath $LogPath) `
        "$HarnessExe did not write a log file at $LogPath (exit code $($proc.ExitCode))"

    $fields = @{}
    Get-Content -LiteralPath $LogPath | ForEach-Object {
        if ($_ -match '^(?<k>[a-z]+)=(?<v>.*)$') {
            $fields[$Matches.k] = $Matches.v
        }
    }
    return $fields
}

function Invoke-FirewallHarness {
    param(
        [Parameter(Mandatory)][string]$HarnessExe,
        [Parameter(Mandatory)][string]$Action,
        [Parameter(Mandatory)][string]$RuleName,
        [Parameter(Mandatory)][string]$ProgramPath,
        [Parameter(Mandatory)][string]$MarkerPath,
        [Parameter(Mandatory)][string]$Run,
        [Parameter(Mandatory)][string]$LogPath
    )
    if (Test-Path -LiteralPath $LogPath) {
        Remove-Item -LiteralPath $LogPath -Force
    }
    $env:MDTEST_ACTION = $Action
    $env:MDTEST_RULE = $RuleName
    $env:MDTEST_PROGRAM = $ProgramPath
    $env:MDTEST_MARKER = $MarkerPath
    $env:MDTEST_RUN = $Run
    $env:MDTEST_LOG = $LogPath
    $proc = Start-Process -FilePath $HarnessExe -PassThru -Wait -WindowStyle Hidden
    Assert (Test-Path -LiteralPath $LogPath) `
        "$HarnessExe did not write a log file at $LogPath (exit code $($proc.ExitCode))"

    $fields = @{}
    Get-Content -LiteralPath $LogPath | ForEach-Object {
        if ($_ -match '^(?<k>[a-z]+)=(?<v>.*)$') {
            $fields[$Matches.k] = $Matches.v
        }
    }
    return $fields
}

function New-MacroDeckFirewallTestProgram([string]$Directory) {
    $withSpace = Join-Path $Directory 'Program Files fake'
    New-Item -ItemType Directory -Force -Path $withSpace | Out-Null
    $target = Join-Path $withSpace 'MacroDeckFirewallTestProbe.exe'
    Copy-Item -LiteralPath $script:PowerShellExe -Destination $target -Force
    return $target
}

Invoke-CompileStep 'host-lock-harness.nsi (normal budget)' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'host-lock-harness.nsi') -Defines @{
        OUTFILE       = $script:HostLockHarnessNormalExe
        PLUGINSDIR_IN = $script:PluginsDir
        TIMEOUT_MS    = $script:NormalTimeoutMs
        POLL_MS       = $script:NormalPollMs
    }
    Assert ($result.ExitCode -eq 0) "makensis failed:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:HostLockHarnessNormalExe) 'compiled but the .exe is missing'
}

Invoke-CompileStep 'host-lock-harness.nsi (short budget)' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'host-lock-harness.nsi') -Defines @{
        OUTFILE       = $script:HostLockHarnessShortExe
        PLUGINSDIR_IN = $script:PluginsDir
        TIMEOUT_MS    = $script:ShortTimeoutMs
        POLL_MS       = $script:ShortPollMs
    }
    Assert ($result.ExitCode -eq 0) "makensis failed:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:HostLockHarnessShortExe) 'compiled but the .exe is missing'
}

Invoke-CompileStep 'mini-upgrade.nsi (v1)' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'mini-upgrade.nsi') -Defines @{
        OUTFILE       = $script:MiniUpgradeV1Exe
        PLUGINSDIR_IN = $script:PluginsDir
        VERSION       = 1
    }
    Assert ($result.ExitCode -eq 0) "makensis failed:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:MiniUpgradeV1Exe) 'compiled but the .exe is missing'
}

Invoke-CompileStep 'mini-upgrade.nsi (v2)' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'mini-upgrade.nsi') -Defines @{
        OUTFILE       = $script:MiniUpgradeV2Exe
        PLUGINSDIR_IN = $script:PluginsDir
        VERSION       = 2
    }
    Assert ($result.ExitCode -eq 0) "makensis failed:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:MiniUpgradeV2Exe) 'compiled but the .exe is missing'
}

Invoke-CompileStep 'firewall-harness.nsi (runas)' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'firewall-harness.nsi') -Defines @{
        OUTFILE = $script:FirewallHarnessExe
    }
    Assert ($result.ExitCode -eq 0) "makensis failed:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:FirewallHarnessExe) 'compiled but the .exe is missing'
}

Invoke-CompileStep 'firewall-harness.nsi (open, unelevated exec path)' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'firewall-harness.nsi') -Defines @{
        OUTFILE                 = $script:FirewallHarnessOpenExe
        MACRODECK_FIREWALL_VERB = 'open'
    }
    Assert ($result.ExitCode -eq 0) "makensis failed:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:FirewallHarnessOpenExe) 'compiled but the .exe is missing'
}

Invoke-Scenario 'HooksHarnessCompiles' {
    $result = Invoke-Makensis -Script (Join-Path $script:TestsDir 'hooks-harness.nsi') -Defines @{
        OUTFILE       = $script:HooksHarnessExe
        PLUGINSDIR_IN = $script:PluginsDir
    }
    Assert ($result.ExitCode -eq 0) "hooks-harness.nsi failed to compile:`n$($result.Output)"
    Assert (Test-Path -LiteralPath $script:HooksHarnessExe) 'hooks-harness.nsi compiled but the .exe is missing'
}

Invoke-Scenario 'NothingRunning' {
    $dir = New-ScenarioDir 'NothingRunning'
    $log = Join-Path $script:LogsDir 'NothingRunning.log'
    $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessNormalExe `
        -ProcessName 'MacroDeckHostNotRunning.exe' -Directory $dir -Scope '1' -LogPath $log
    Assert-Field $fields 'result' '0'
}

Invoke-Scenario 'HostDirectoryAbsent' {
    $dir = Join-Path $script:ScenariosDir 'HostDirectoryAbsent-missing'
    $log = Join-Path $script:LogsDir 'HostDirectoryAbsent.log'
    $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessNormalExe `
        -ProcessName 'MacroDeckHostNotRunning.exe' -Directory $dir -Scope '1' -LogPath $log
    Assert-Field $fields 'result' '0'
}

Invoke-Scenario 'SingleHostProcess' {
    $dir = New-ScenarioDir 'SingleHostProcess'
    $proc = Start-FakeHostProcess -Directory $dir -ExeName 'MacroDeckHostTest.exe'
    try {
        $log = Join-Path $script:LogsDir 'SingleHostProcess.log'
        $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessNormalExe `
            -ProcessName 'MacroDeckHostTest.exe' -Directory $dir -Scope '1' -LogPath $log
        Assert-Field $fields 'result' '0'
        Assert (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) 'the fake host process should be gone'
        Remove-Item -LiteralPath (Join-Path $dir 'MacroDeckHostTest.exe') -Force
    } finally {
        Stop-ProcessSafely $proc
    }
}

Invoke-Scenario 'MultipleMatchingProcesses' {
    $dir = New-ScenarioDir 'MultipleMatchingProcesses'
    $exeName = 'MacroDeckHostTest.exe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $target = Join-Path $dir $exeName
    Copy-Item -LiteralPath $script:PowerShellExe -Destination $target -Force
    $procs = 1..3 | ForEach-Object {
        Start-Process -FilePath $target `
            -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 300') `
            -PassThru -WindowStyle Hidden
    }
    try {
        $log = Join-Path $script:LogsDir 'MultipleMatchingProcesses.log'
        $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessNormalExe `
            -ProcessName $exeName -Directory $dir -Scope '1' -LogPath $log
        Assert-Field $fields 'result' '0'
        foreach ($p in $procs) {
            Assert (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) "process $($p.Id) should be gone"
        }
    } finally {
        foreach ($p in $procs) { Stop-ProcessSafely $p }
    }
}

Invoke-Scenario 'DelayedLockRelease' {
    $dir = New-ScenarioDir 'DelayedLockRelease'
    $hostProc = Start-FakeHostProcess -Directory $dir -ExeName 'MacroDeckHostTest.exe'
    $lockFile = Join-Path $dir 'Cronos.dll'
    Set-Content -LiteralPath $lockFile -Value 'stub' -NoNewline
    $holdSeconds = 3
    $holder = Start-FileHolder -Path $lockFile -HoldSeconds $holdSeconds
    try {
        Wait-UntilLocked -Path $lockFile
        # The holder starts its countdown once it holds the lock, so the release lands at least
        # $holdSeconds after the lock was observed here, minus the 50ms poll that observed it.
        $lockObservedAt = Get-Date
        $log = Join-Path $script:LogsDir 'DelayedLockRelease.log'
        $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessNormalExe `
            -ProcessName 'MacroDeckHostTest.exe' -Directory $dir -Scope '1' -LogPath $log
        $waited = ((Get-Date) - $lockObservedAt).TotalMilliseconds
        Assert-Field $fields 'result' '0'
        # Wall clock, not the harness's own 'elapsed': that field sums only the Sleep calls, so the
        # kill/find/probe work of every pass is missing from it, and a loaded runner undercounts a
        # real three-second wait far enough to trip a fixed threshold on it (it reported 1800).
        $minimumWaitMs = ($holdSeconds * 1000) - 500
        Assert ($waited -ge $minimumWaitMs) `
            "expected the harness to wait for the release (>= $minimumWaitMs ms) but it returned after $waited ms"
        Assert ([int]$fields['elapsed'] -gt 0) `
            "expected the harness to report the time it spent waiting but got $($fields['elapsed'])"
    } finally {
        Stop-ProcessSafely $hostProc
        $holder.WaitForExit(5000)
        Stop-ProcessSafely $holder
    }
}

Invoke-Scenario 'LockNeverReleased' {
    $dir = New-ScenarioDir 'LockNeverReleased'
    $hostProc = Start-FakeHostProcess -Directory $dir -ExeName 'MacroDeckHostTest.exe'
    $lockFile = Join-Path $dir 'Cronos.dll'
    Set-Content -LiteralPath $lockFile -Value 'stub' -NoNewline
    $holder = Start-FileHolder -Path $lockFile -HoldSeconds 120
    try {
        Wait-UntilLocked -Path $lockFile
        $log = Join-Path $script:LogsDir 'LockNeverReleased.log'
        $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessShortExe `
            -ProcessName 'MacroDeckHostTest.exe' -Directory $dir -Scope '1' -LogPath $log
        Assert-Field $fields 'result' '2'
        Assert-Field $fields 'locked' 'Cronos.dll'
        Assert ([int]$fields['elapsed'] -ge $script:ShortTimeoutMs) `
            "expected elapsed >= $script:ShortTimeoutMs but got $($fields['elapsed'])"
    } finally {
        Stop-ProcessSafely $hostProc
        Stop-ProcessSafely $holder
    }
}

Invoke-Scenario 'KillReportsSuccessButFileStaysLocked' {
    $dir = New-ScenarioDir 'KillReportsSuccessButFileStaysLocked'
    $lockFile = Join-Path $dir 'Cronos.dll'
    Set-Content -LiteralPath $lockFile -Value 'stub' -NoNewline
    $holder = Start-FileHolder -Path $lockFile -HoldSeconds 120
    try {
        Wait-UntilLocked -Path $lockFile
        $log = Join-Path $script:LogsDir 'KillReportsSuccessButFileStaysLocked.log'
        $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessShortExe `
            -ProcessName 'MacroDeckHostNeverExisted.exe' -Directory $dir -Scope '1' -LogPath $log
        Assert-Field $fields 'result' '2'
        Assert-Field $fields 'kill' '2'
        Assert-Field $fields 'locked' 'Cronos.dll'
    } finally {
        Stop-ProcessSafely $holder
    }
}

Invoke-Scenario 'TopLevelOnly' {
    $dir = New-ScenarioDir 'TopLevelOnly'
    $wwwroot = Join-Path $dir 'wwwroot'
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'top-level-file.txt') -Value 'writable' -NoNewline
    $lockFile = Join-Path $wwwroot 'locked-asset.bin'
    Set-Content -LiteralPath $lockFile -Value 'stub' -NoNewline
    $holder = Start-FileHolder -Path $lockFile -HoldSeconds 120
    try {
        Wait-UntilLocked -Path $lockFile
        $log = Join-Path $script:LogsDir 'TopLevelOnly.log'
        $fields = Invoke-HostLockHarness -HarnessExe $script:HostLockHarnessNormalExe `
            -ProcessName 'MacroDeckHostNotRunning.exe' -Directory $dir -Scope '1' -LogPath $log
        Assert-Field $fields 'result' '0'
    } finally {
        Stop-ProcessSafely $holder
    }
}

Invoke-Scenario 'InPlaceUpgrade' {
    Assert ($script:CompileStatus['mini-upgrade.nsi (v1)'] -eq $true) 'mini-upgrade v1 did not compile'
    Assert ($script:CompileStatus['mini-upgrade.nsi (v2)'] -eq $true) 'mini-upgrade v2 did not compile'

    $scratch = New-ScenarioDir 'InPlaceUpgrade'
    $installDir = Join-Path $scratch 'install'

    $v1 = Invoke-Installer -Installer $script:MiniUpgradeV1Exe -InstallDir $installDir
    Assert ($v1.ExitCode -eq 0) "mini-upgrade v1 install failed with exit code $($v1.ExitCode)"

    $hostDir = Join-Path $installDir 'host'
    Assert (Test-Path -LiteralPath (Join-Path $hostDir 'version.txt')) 'v1 install did not write host\version.txt'

    $hostProc = Start-FakeHostProcess -Directory $hostDir -ExeName 'MacroDeckHost.exe'
    try {
        $v2 = Invoke-Installer -Installer $script:MiniUpgradeV2Exe -InstallDir $installDir
        Assert ($v2.ExitCode -eq 0) "mini-upgrade v2 install failed with exit code $($v2.ExitCode)"

        $version = (Get-Content -LiteralPath (Join-Path $hostDir 'version.txt') -Raw).Trim()
        Assert ($version -eq '2') "expected host\version.txt to read 2 after the upgrade but got '$version'"
        Assert (-not (Get-Process -Name 'MacroDeckHost' -ErrorAction SilentlyContinue)) `
            'no MacroDeckHost process should remain after the upgrade'
    } finally {
        Stop-ProcessSafely $hostProc
        Get-Process -Name 'MacroDeckHost' -ErrorAction SilentlyContinue | ForEach-Object { Stop-ProcessSafely $_ }
    }
}

Invoke-Scenario 'FirewallAddCommandShape' {
    $dir = New-ScenarioDir 'FirewallAddCommandShape'
    $log = Join-Path $script:LogsDir 'FirewallAddCommandShape.log'
    $program = Join-Path $dir 'Program Files fake\MacroDeckHost.exe'
    $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
        -RuleName 'MacroDeckFirewallTest-Command' -ProgramPath $program `
        -MarkerPath (Join-Path $dir 'fw.ok') -Run '0' -LogPath $log
    Assert ($fields.ContainsKey('command')) 'log is missing the command field'
    $command = $fields['command']
    Assert ($command.Contains("program=`"$program`"")) "command does not quote the program path: $command"
    foreach ($needle in @('dir=in', 'action=allow', 'enable=yes', 'profile=any', 'protocol=TCP', '/D /S /C', 'MacroDeckFirewallTest-Command')) {
        Assert ($command.Contains($needle)) "command is missing '$needle': $command"
    }
    Assert-OuterQuotePair $command
}

Invoke-Scenario 'FirewallDeleteRunsBeforeAdd' {
    $dir = New-ScenarioDir 'FirewallDeleteRunsBeforeAdd'
    $log = Join-Path $script:LogsDir 'FirewallDeleteRunsBeforeAdd.log'
    $program = Join-Path $dir 'MacroDeckHost.exe'
    $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
        -RuleName 'MacroDeckFirewallTest-Order' -ProgramPath $program `
        -MarkerPath (Join-Path $dir 'fw.ok') -Run '0' -LogPath $log
    $command = $fields['command']
    $deleteIndex = $command.IndexOf('firewall delete rule')
    $addIndex = $command.IndexOf('firewall add rule')
    Assert ($deleteIndex -ge 0 -and $addIndex -ge 0) "command is missing delete or add: $command"
    Assert ($deleteIndex -lt $addIndex) "delete must run before add, or an upgrade stacks duplicate rules: $command"
}

Invoke-Scenario 'FirewallDeleteClauseMatchesProgramNotDisplayName' {
    $dir = New-ScenarioDir 'FirewallDeleteClauseMatchesProgramNotDisplayName'
    $log = Join-Path $script:LogsDir 'FirewallDeleteClauseMatchesProgramNotDisplayName.log'
    $program = Join-Path $dir 'MacroDeckHost.exe'
    $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
        -RuleName 'MacroDeckFirewallTest-DeleteClause' -ProgramPath $program `
        -MarkerPath (Join-Path $dir 'fw.ok') -Run '0' -LogPath $log
    Assert ($fields['command'].Contains('delete rule name=all dir=in program=')) `
        "command must delete by program, not display name: $($fields['command'])"
}

Invoke-Scenario 'FirewallCommandNeverPinsLocalPort' {
    $dir = New-ScenarioDir 'FirewallCommandNeverPinsLocalPort'
    $log = Join-Path $script:LogsDir 'FirewallCommandNeverPinsLocalPort.log'
    $program = Join-Path $dir 'MacroDeckHost.exe'
    $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
        -RuleName 'MacroDeckFirewallTest-NoPort' -ProgramPath $program `
        -MarkerPath (Join-Path $dir 'fw.ok') -Run '0' -LogPath $log
    Assert (-not $fields['command'].Contains('localport')) `
        "command must never pin localport, or MACRO_DECK_PORT overrides stop matching: $($fields['command'])"
}

Invoke-Scenario 'FirewallRemoveCommandShape' {
    $dir = New-ScenarioDir 'FirewallRemoveCommandShape'
    $log = Join-Path $script:LogsDir 'FirewallRemoveCommandShape.log'
    $program = Join-Path $dir 'MacroDeckHost.exe'
    $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'remove' `
        -RuleName 'MacroDeckFirewallTest-Remove' -ProgramPath $program `
        -MarkerPath (Join-Path $dir 'fw.ok') -Run '0' -LogPath $log
    $command = $fields['command']
    Assert-OuterQuotePair $command
    Assert (-not $command.Contains('add rule')) "remove command must not add a rule: $command"
    Assert (-not $command.Contains('&&')) `
        "remove must join the marker with & not && - netsh's exit code 1 for 'no rules matched' is a normal uninstall outcome, not a failure: $command"
    Assert ($command.Contains('& >"')) "remove command must join the marker write with a plain &: $command"
}

Invoke-Scenario 'FirewallAddDeniedWithoutElevationReportsNoMarker' `
    -SkipWhen (Test-Elevated) `
    -SkipReason 'requires a non-elevated shell so the elevated netsh add is actually denied' {
    $dir = New-ScenarioDir 'FirewallAddDeniedWithoutElevationReportsNoMarker'
    $program = New-MacroDeckFirewallTestProgram $dir
    $log = Join-Path $script:LogsDir 'FirewallAddDeniedWithoutElevationReportsNoMarker.log'
    $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessOpenExe -Action 'add' `
        -RuleName 'MacroDeckFirewallTest-Unelevated' -ProgramPath $program `
        -MarkerPath (Join-Path $dir 'fw.ok') -Run '1' -LogPath $log
    Assert-Field $fields 'result' '2'
}

try {
    Get-NetFirewallRule -DisplayName 'MacroDeckFirewallTest-*' -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
} catch {
    Write-Host "==> Could not sweep leftover test firewall rules: $($_.Exception.Message)"
}

Invoke-Scenario 'FirewallAddCreatesExactlyOneEnabledInboundAllowRule' `
    -SkipWhen (-not (Test-Elevated)) -SkipReason 'requires an elevated shell' {
    $ruleName = "MacroDeckFirewallTest-$([Guid]::NewGuid())"
    $dir = New-ScenarioDir 'FirewallAddCreatesExactlyOneEnabledInboundAllowRule'
    $program = New-MacroDeckFirewallTestProgram $dir
    try {
        $log = Join-Path $script:LogsDir 'FirewallAddCreatesExactlyOneEnabledInboundAllowRule.log'
        $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath (Join-Path $dir 'fw.ok') `
            -Run '1' -LogPath $log
        Assert-Field $fields 'result' '0'

        $rules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
        Assert ($rules.Count -eq 1) "expected exactly one rule named $ruleName but found $($rules.Count)"
        Assert ($rules[0].Enabled -eq 'True') 'rule must be enabled'
        Assert ($rules[0].Direction -eq 'Inbound') 'rule must be inbound'
        Assert ($rules[0].Action -eq 'Allow') 'rule must allow'
        $profile = [string]$rules[0].Profile
        $profiles = $profile -split ', '
        foreach ($p in @('Domain', 'Private', 'Public')) {
            Assert ($profile -eq 'Any' -or $profiles -contains $p) "rule profile must cover $p, got $profile"
        }
    } finally {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
}

Invoke-Scenario 'FirewallAddSetsExactProgramPath' `
    -SkipWhen (-not (Test-Elevated)) -SkipReason 'requires an elevated shell' {
    $ruleName = "MacroDeckFirewallTest-$([Guid]::NewGuid())"
    $dir = New-ScenarioDir 'FirewallAddSetsExactProgramPath'
    $program = New-MacroDeckFirewallTestProgram $dir
    try {
        $log = Join-Path $script:LogsDir 'FirewallAddSetsExactProgramPath.log'
        $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath (Join-Path $dir 'fw.ok') `
            -Run '1' -LogPath $log
        Assert-Field $fields 'result' '0'

        $rule = Get-NetFirewallRule -DisplayName $ruleName
        $appFilter = $rule | Get-NetFirewallApplicationFilter
        Assert ($appFilter.Program -eq $program) "expected program $program but got $($appFilter.Program)"
    } finally {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
}

Invoke-Scenario 'FirewallAddSetsTcpAnyPort' `
    -SkipWhen (-not (Test-Elevated)) -SkipReason 'requires an elevated shell' {
    $ruleName = "MacroDeckFirewallTest-$([Guid]::NewGuid())"
    $dir = New-ScenarioDir 'FirewallAddSetsTcpAnyPort'
    $program = New-MacroDeckFirewallTestProgram $dir
    try {
        $log = Join-Path $script:LogsDir 'FirewallAddSetsTcpAnyPort.log'
        $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath (Join-Path $dir 'fw.ok') `
            -Run '1' -LogPath $log
        Assert-Field $fields 'result' '0'

        $rule = Get-NetFirewallRule -DisplayName $ruleName
        $portFilter = $rule | Get-NetFirewallPortFilter
        Assert ($portFilter.Protocol -eq 'TCP') "expected TCP but got $($portFilter.Protocol)"
        Assert ($portFilter.LocalPort -eq 'Any') "expected LocalPort Any but got $($portFilter.LocalPort)"
    } finally {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
}

Invoke-Scenario 'FirewallSecondAddDoesNotDuplicate' `
    -SkipWhen (-not (Test-Elevated)) -SkipReason 'requires an elevated shell' {
    $ruleName = "MacroDeckFirewallTest-$([Guid]::NewGuid())"
    $dir = New-ScenarioDir 'FirewallSecondAddDoesNotDuplicate'
    $program = New-MacroDeckFirewallTestProgram $dir
    try {
        $marker = Join-Path $dir 'fw.ok'
        $log1 = Join-Path $script:LogsDir 'FirewallSecondAddDoesNotDuplicate-1.log'
        $fields1 = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath $marker -Run '1' -LogPath $log1
        Assert-Field $fields1 'result' '0'

        $log2 = Join-Path $script:LogsDir 'FirewallSecondAddDoesNotDuplicate-2.log'
        $fields2 = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath $marker -Run '1' -LogPath $log2
        Assert-Field $fields2 'result' '0'

        $rules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
        Assert ($rules.Count -eq 1) "a second add must not duplicate the rule, found $($rules.Count)"
    } finally {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
}

Invoke-Scenario 'FirewallRemoveDeletesTheRule' `
    -SkipWhen (-not (Test-Elevated)) -SkipReason 'requires an elevated shell' {
    $ruleName = "MacroDeckFirewallTest-$([Guid]::NewGuid())"
    $dir = New-ScenarioDir 'FirewallRemoveDeletesTheRule'
    $program = New-MacroDeckFirewallTestProgram $dir
    try {
        $marker = Join-Path $dir 'fw.ok'
        $addLog = Join-Path $script:LogsDir 'FirewallRemoveDeletesTheRule-add.log'
        $addFields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'add' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath $marker -Run '1' -LogPath $addLog
        Assert-Field $addFields 'result' '0'
        $rulesBeforeRemove = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
        Assert ($rulesBeforeRemove.Count -gt 0) 'the rule must exist before removal'

        $removeLog = Join-Path $script:LogsDir 'FirewallRemoveDeletesTheRule-remove.log'
        $removeFields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'remove' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath $marker -Run '1' -LogPath $removeLog
        Assert-Field $removeFields 'result' '0'
        $rulesAfterRemove = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
        Assert ($rulesAfterRemove.Count -eq 0) 'the rule must be gone after remove'
    } finally {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
}

Invoke-Scenario 'FirewallRemoveOfNeverCreatedRuleStillReportsOk' `
    -SkipWhen (-not (Test-Elevated)) -SkipReason 'requires an elevated shell' {
    $ruleName = "MacroDeckFirewallTest-$([Guid]::NewGuid())"
    $dir = New-ScenarioDir 'FirewallRemoveOfNeverCreatedRuleStillReportsOk'
    $program = New-MacroDeckFirewallTestProgram $dir
    try {
        $log = Join-Path $script:LogsDir 'FirewallRemoveOfNeverCreatedRuleStillReportsOk.log'
        $fields = Invoke-FirewallHarness -HarnessExe $script:FirewallHarnessExe -Action 'remove' `
            -RuleName $ruleName -ProgramPath $program -MarkerPath (Join-Path $dir 'fw.ok') `
            -Run '1' -LogPath $log
        Assert-Field $fields 'result' '0'
    } finally {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host '=== Installer test summary ==='
foreach ($r in $script:Results) {
    Write-Host ("{0,-55} {1}" -f $r.Name, $r.Status.ToUpperInvariant())
    if ($r.Status -ne 'Pass') {
        Write-Host "    $($r.Detail)"
    }
}

if ($env:GITHUB_STEP_SUMMARY) {
    $lines = @('## Installer tests', '', '| Scenario | Result | Detail |', '| --- | --- | --- |')
    foreach ($r in $script:Results) {
        $status = switch ($r.Status) {
            'Pass' { ':white_check_mark: Pass' }
            'Skip' { ':fast_forward: Skipped' }
            default { ':x: Fail' }
        }
        $detail = ($r.Detail -replace '\|', '\|') -replace '\r?\n', ' '
        $lines += "| $($r.Name) | $status | $detail |"
    }
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($lines -join "`n")
}

$failures = @($script:Results | Where-Object { $_.Status -eq 'Fail' })
if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "$($failures.Count) of $($script:Results.Count) check(s) failed."
    exit 1
}

$skipped = @($script:Results | Where-Object { $_.Status -eq 'Skip' })
Write-Host ''
Write-Host "$($script:Results.Count - $skipped.Count) of $($script:Results.Count) checks passed, $($skipped.Count) skipped."
exit 0

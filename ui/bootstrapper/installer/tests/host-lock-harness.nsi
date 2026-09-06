; Compiles ..\host-lock.nsh directly - no ${PRODUCTNAME}, ${MAINBINARYNAME}, ${INSTALLMODE},
; $PassiveMode, CheckIfAppIsRunning, MessageBox or Abort - so Invoke-InstallerTests.ps1 can run
; the kill/verify loop against real Windows processes across a range of scenarios without any
; of the Tauri template's symbols.
;
; The driver asserts on the log file written to MDTEST_LOG, not the exit code: SetErrorLevel
; communicates $MacroDeckStopResult for convenience, but the log is what carries every field
; (result/locked/kill/elapsed) the scenarios actually check.
;
; CurrentUserOnly is consumed by host-lock.nsh's macro through a compile-time !if, so it cannot
; be a runtime variable at the !insertmacro call site. MDTEST_SCOPE is read at runtime and used
; to pick between two !insertmacro call sites (literal "1" and literal "0"), so both branches -
; current-user-only and all-users - still get compiled and exercised.

Unicode true
Name "Macro Deck host-lock harness"
OutFile "${OUTFILE}"
InstallDir "$PLUGINSDIR\macro-deck-host-lock"
RequestExecutionLevel user
SilentInstall silent
ShowInstDetails nevershow

!addplugindir /x86-unicode "${PLUGINSDIR_IN}"

!include "..\host-lock.nsh"

!ifndef TIMEOUT_MS
!define TIMEOUT_MS 5000
!endif
!ifndef POLL_MS
!define POLL_MS 100
!endif

Var MDTestProcess
Var MDTestDir
Var MDTestLog
Var MDTestScope
Var MDTestLogHandle

Section Install
	ReadEnvStr $MDTestProcess "MDTEST_PROCESS"
	ReadEnvStr $MDTestDir "MDTEST_DIR"
	ReadEnvStr $MDTestLog "MDTEST_LOG"
	ReadEnvStr $MDTestScope "MDTEST_SCOPE"

	${If} $MDTestScope == "1"
		!insertmacro MacroDeckStopProcessAndWait "$MDTestProcess" "$MDTestDir" "1" ${TIMEOUT_MS} ${POLL_MS}
	${Else}
		!insertmacro MacroDeckStopProcessAndWait "$MDTestProcess" "$MDTestDir" "0" ${TIMEOUT_MS} ${POLL_MS}
	${EndIf}

	FileOpen $MDTestLogHandle "$MDTestLog" w
	FileWrite $MDTestLogHandle "result=$MacroDeckStopResult$\r$\n"
	FileWrite $MDTestLogHandle "locked=$MacroDeckLockedFile$\r$\n"
	FileWrite $MDTestLogHandle "kill=$MacroDeckKillStatus$\r$\n"
	FileWrite $MDTestLogHandle "elapsed=$MacroDeckStopElapsed$\r$\n"
	FileClose $MDTestLogHandle

	SetErrorLevel $MacroDeckStopResult
SectionEnd

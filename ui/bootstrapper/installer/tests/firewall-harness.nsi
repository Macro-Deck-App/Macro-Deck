; Compiles ..\firewall.nsh directly - no ${PRODUCTNAME}, ${MAINBINARYNAME}, ${INSTALLMODE},
; $PassiveMode, CheckIfAppIsRunning, MessageBox or Abort - so Invoke-InstallerTests.ps1 can build
; the elevated netsh command and, on an elevated shell, exercise it against real Windows Firewall
; rules, without any of the Tauri template's symbols.
;
; The driver asserts on the log file written to MDTEST_LOG, not the exit code: SetErrorLevel
; communicates $MacroDeckFirewallResult for convenience, but the log is what carries both the
; result and the built command string the scenarios actually check.
;
; Action is consumed by firewall.nsh's MacroDeckBuildFirewallCommand macro through a compile-time
; !if, so it cannot be a runtime variable at the !insertmacro call site. MDTEST_ACTION is read at
; runtime and used to pick between two !insertmacro call sites (literal "add" and literal
; "remove"), so both branches still get compiled and exercised - the same trick
; host-lock-harness.nsi uses for MDTEST_SCOPE, to still cover both the current-user-only and
; all-users branches even though CurrentUserOnly is itself a compile-time !if there too.
;
; Unlike the other two harnesses, this one does not use nsis_tauri_utils, so it needs neither
; !addplugindir nor PLUGINSDIR_IN.

Unicode true
Name "Macro Deck firewall harness"
OutFile "${OUTFILE}"
InstallDir "$TEMP\macro-deck-firewall-harness"
RequestExecutionLevel user
SilentInstall silent
ShowInstDetails nevershow

!include "..\firewall.nsh"

Var MDTestAction
Var MDTestRule
Var MDTestProgram
Var MDTestMarker
Var MDTestRun
Var MDTestLog
Var MDTestLogHandle

Section Install
	ReadEnvStr $MDTestAction "MDTEST_ACTION"
	ReadEnvStr $MDTestRule "MDTEST_RULE"
	ReadEnvStr $MDTestProgram "MDTEST_PROGRAM"
	ReadEnvStr $MDTestMarker "MDTEST_MARKER"
	ReadEnvStr $MDTestRun "MDTEST_RUN"
	ReadEnvStr $MDTestLog "MDTEST_LOG"

	; Recognisable before any run, so a non-run build (MDTEST_RUN=0, which only builds the
	; command string) reports it unambiguously rather than looking like a leftover
	; MACRODECK_FIREWALL_OK from a previous invocation.
	StrCpy $MacroDeckFirewallResult "-1"

	${If} $MDTestAction == "add"
		!insertmacro MacroDeckBuildFirewallCommand "add" "$MDTestRule" "$MDTestProgram" "$MDTestMarker"
	${Else}
		!insertmacro MacroDeckBuildFirewallCommand "remove" "$MDTestRule" "$MDTestProgram" "$MDTestMarker"
	${EndIf}

	${If} $MDTestRun == "1"
		!insertmacro MacroDeckRunFirewallCommand "$MDTestMarker"
	${EndIf}

	FileOpen $MDTestLogHandle "$MDTestLog" w
	FileWrite $MDTestLogHandle "result=$MacroDeckFirewallResult$\r$\n"
	FileWrite $MDTestLogHandle "command=$MacroDeckFirewallCommand$\r$\n"
	FileClose $MDTestLogHandle

	SetErrorLevel $MacroDeckFirewallResult
SectionEnd

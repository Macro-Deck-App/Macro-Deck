; Building and running the elevated Windows Firewall command for the inbound rule the installer
; creates so Windows does not have to prompt for one itself the first time the .NET host binds
; its public port (issue #345).
;
; Deliberately independent of the Tauri NSIS template - no ${PRODUCTNAME}, ${MAINBINARYNAME},
; ${INSTALLMODE}, $PassiveMode, CheckIfAppIsRunning, MessageBox or Abort - so
; installer/tests/firewall-harness.nsi can compile it on its own and CI can exercise the command
; string and, where the runner is elevated, the real netsh calls, without any of the template's
; symbols. installer_config.rs asserts that independence the same way it does for host-lock.nsh.
;
; Results are globals rather than stack values, matching host-lock.nsh: this macro pair is
; inserted into more than one section, and the Exch juggling a stack result needs is exactly what
; regresses unnoticed. Neither macro touches a scratch register, so callers keep $0-$9.

!ifndef MACRODECK_FIREWALL_NSH
!define MACRODECK_FIREWALL_NSH

!include LogicLib.nsh

Var MacroDeckFirewallCommand
Var MacroDeckFirewallResult

!define MACRODECK_FIREWALL_OK 0
!define MACRODECK_FIREWALL_LAUNCH_FAILED 1   ; UAC declined, no admin account, policy
!define MACRODECK_FIREWALL_NO_MARKER 2       ; the elevated command ran but netsh failed

; !ifndef so installer/tests/firewall-harness.nsi can compile a /DMACRODECK_FIREWALL_VERB=open
; build: "open" runs the same command unelevated, which is the only way to exercise the execute
; path on a CI runner without hanging on a UAC prompt nobody can click. A release build must never
; override this - installer_config.rs pins the shipped verb to "runas".
!ifndef MACRODECK_FIREWALL_VERB
!define MACRODECK_FIREWALL_VERB "runas"
!endif

; Sets $MacroDeckFirewallCommand to a cmd.exe parameter string that deletes any existing
; program-scoped rule and, for "add", recreates it before dropping MarkerPath - the caller's only
; way to tell the elevated command actually ran to completion rather than merely being launched.
;
; Action is "add" or "remove" and is consumed below through a compile-time !if: this macro can be
; inserted into more than one section (install and uninstall), so Action cannot be a runtime
; variable at the call site the way RuleName/ProgramPath/MarkerPath are - see
; firewall-harness.nsi for how the harness still exercises both branches, by picking between two
; literal !insertmacro call sites at runtime.
!macro MacroDeckBuildFirewallCommand Action RuleName ProgramPath MarkerPath
	; Single-quoted NSIS strings so the embedded " characters below are literal; $ variables and
	; ${...} macro parameters still expand inside a single-quoted string, only the delimiter
	; character changes.
	;
	; /D suppresses HKCU\...\Command Processor\AutoRun, which cmd.exe would otherwise run
	; elevated before a single character of this command executes - a local privilege-escalation
	; hole for anything that can write that key as the current user. /S makes cmd strip exactly
	; the outer quote pair (the one right after /C and the very last one) and take the rest of
	; the string verbatim, which is what makes an inline command with its own embedded quoted
	; paths deterministic instead of subject to cmd's usual, ambiguous quote-counting rules.
	;
	; "delete rule name=all dir=in program=" rather than matching the display name: Windows
	; writes *block* rules for a program when the user dismisses its own security alert with
	; Cancel, and block wins over allow, so a name-scoped delete would leave a new allow rule
	; silently overridden for exactly the users this issue is about. Matching on the program path
	; is also multi-user-safe (per-user installs live under distinct %LOCALAPPDATA% paths, so one
	; account's uninstall never removes another's rule) and is what keeps an upgrade from
	; stacking duplicates.
	StrCpy $MacroDeckFirewallCommand '/D /S /C ""$SYSDIR\netsh.exe" advfirewall firewall delete rule name=all dir=in program="${ProgramPath}"'

	!if "${Action}" == "add"
		; profile=any is Domain+Private+Public in one rule - the issue's "all network profiles",
		; so a LAN Windows misclassifies as Public still works. Program-scoped, never localport=:
		; the public port is overridable through MACRO_DECK_PORT, so a port-pinned rule would
		; silently stop matching after an override.
		StrCpy $MacroDeckFirewallCommand '$MacroDeckFirewallCommand & "$SYSDIR\netsh.exe" advfirewall firewall add rule name="${RuleName}" dir=in action=allow program="${ProgramPath}" enable=yes profile=any protocol=TCP'
		StrCpy $MacroDeckFirewallCommand '$MacroDeckFirewallCommand && >"${MarkerPath}" echo ok"'
	!else
		; & rather than && after the delete: "netsh ... delete rule" exits 1 with "No rules
		; match the specified criteria", which is a normal successful outcome for an uninstall -
		; nothing was ever added, or the user declined UAC at install time. && here would turn
		; that ordinary case into a spurious MACRODECK_FIREWALL_NO_MARKER.
		StrCpy $MacroDeckFirewallCommand '$MacroDeckFirewallCommand & >"${MarkerPath}" echo ok"'
	!endif

	; One NSIS string against NSIS_MAX_STRLEN 1024, which truncates silently rather than
	; erroring. Worst realistic case - a user-chosen $INSTDIR near MAX_PATH, which appears twice
	; above - is still only around 900 characters, hence the deliberately short marker filename
	; ("fw.ok") at every call site.
!macroend

; Launches the built command elevated (or via ${MACRODECK_FIREWALL_VERB} "open" in the harness)
; and classifies the outcome into $MacroDeckFirewallResult.
;
; A declined UAC prompt, a missing administrator account to elevate to, or a policy that blocks
; elevation never reaches netsh at all, and ExecShellWait's only signal for any of that is the
; NSIS error flag - it never surfaces cmd.exe's own exit code for a "runas" verb, so that is the
; only way LAUNCH_FAILED is detectable. NO_MARKER means the elevated cmd.exe genuinely ran but
; the netsh calls inside it failed before writing MarkerPath.
!macro MacroDeckRunFirewallCommand MarkerPath
	Delete "${MarkerPath}"
	ClearErrors
	ExecShellWait "${MACRODECK_FIREWALL_VERB}" "$SYSDIR\cmd.exe" "$MacroDeckFirewallCommand" SW_HIDE
	${If} ${Errors}
		StrCpy $MacroDeckFirewallResult ${MACRODECK_FIREWALL_LAUNCH_FAILED}
	${Else}
		${If} ${FileExists} "${MarkerPath}"
			StrCpy $MacroDeckFirewallResult ${MACRODECK_FIREWALL_OK}
		${Else}
			StrCpy $MacroDeckFirewallResult ${MACRODECK_FIREWALL_NO_MARKER}
		${EndIf}
	${EndIf}
	Delete "${MarkerPath}"
!macroend

!endif ; MACRODECK_FIREWALL_NSH

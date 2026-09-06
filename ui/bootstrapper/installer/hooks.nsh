; Installer hooks (bundle.windows.nsis.installerHooks). The Tauri NSIS template only looks for the
; main binary (MacroDeck.exe) when it checks for a running app. Macro Deck also installs the .NET
; host, and a live host locks its own image file, so both passes of an update fail while it runs:
; the previous uninstaller cannot remove host\ and the install cannot write it (issue #131).
;
; The bootstrapper stops the host itself before an update installs; this is the safety net for a
; host that was orphaned (a crashed shell never got to stop it) and for an installer the user
; starts by hand while Macro Deck is running.
;
; The kill/verify loop lives in host-lock.nsh, and the elevated firewall-rule command lives in
; firewall.nsh, so installer/tests/ can compile and run each without the template's defines.
; Everything template-shaped stays here.
;
; ${__FILEDIR__} is required: !include resolves a relative path against makensis' working
; directory, which is the bundler's output directory, not this one.
!include "${__FILEDIR__}\host-lock.nsh"
!include "${__FILEDIR__}\firewall.nsh"

; NSIS offers Abort/Retry/Ignore when a File command cannot write its target, and Ignore is how
; issue #131 produced a half-updated installation. Off leaves Retry and Cancel. This is a compiler
; flag and applies to every line below it; the template includes this file above all sections, so
; it covers every File command. NSIS cannot roll back, so this is only the last-resort net - the
; pre-install abort below is what actually keeps an installation from being mixed.
AllowSkipFiles off

; Budget for the host to disappear and release host\. Generous because an on-access virus scanner
; can hold a freshly written file well past the process teardown.
!define MACRODECK_HOST_TIMEOUT_MS 30000
!define MACRODECK_HOST_POLL_MS 250

; Selects the installed host apphost name from the product identity. Must stay the first !macro
; in this file: installer_config.rs::nsis_hooks_stop_the_host_process asserts the first "!macro "
; byte offset precedes the first ${PRODUCTNAME} byte offset, which is the root-cause guard for
; issue #131 below.
;
; The selector has to be evaluated inside a macro body, expanded at !insertmacro time, rather
; than at file level: the template includes this file before it defines PRODUCTNAME, so a
; top-level !if would silently compare against an undefined value and every release installer
; would end up hunting the Development host, which never exists (the issue #131 regression). By
; the time a macro body expands, the template's own !define is in scope.
!macro MacroDeckSelectHostBinary
	!if "${PRODUCTNAME}" == "Macro Deck"
		!define MACRODECK_HOST_BINARY "MacroDeckHost.exe"
	!else
		!define MACRODECK_HOST_BINARY "MacroDeckHostDevelopment.exe"
	!endif
!macroend

!macro MacroDeckReleaseHostBinary
	!undef MACRODECK_HOST_BINARY
!macroend

!macro MacroDeckStopProcesses
	!define MDHOOK ${__LINE__}

	!insertmacro MacroDeckSelectHostBinary
	!if "${INSTALLMODE}" == "currentUser"
		!define MACRODECK_HOST_SCOPE "1"
	!else
		!define MACRODECK_HOST_SCOPE "0"
	!endif

	Push $0
	Push $1

macrodeck_host_attempt_${MDHOOK}:
	; Stop the shell first - it owns the host and would otherwise report the killed host as an
	; unexpected crash. The template's own check, so the user still gets the usual prompt, and
	; the template's copy right after this hook then finds nothing left to do. Inside the retry
	; loop because a Retry is worthless if the shell came back meanwhile; it is a no-op when
	; nothing matches.
	!insertmacro CheckIfAppIsRunning "${MAINBINARYNAME}.exe" "${PRODUCTNAME}"

	DetailPrint "Waiting for ${MACRODECK_HOST_BINARY} to stop..."
	; $INSTDIR\host, never $INSTDIR: during an in-place upgrade the old uninstaller runs from
	; the install directory (_?=$INSTDIR) and locks its own image, so probing $INSTDIR would
	; make the pre-uninstall hook wait for itself.
	!insertmacro MacroDeckStopProcessAndWait "${MACRODECK_HOST_BINARY}" "$INSTDIR\host" \
		"${MACRODECK_HOST_SCOPE}" ${MACRODECK_HOST_TIMEOUT_MS} ${MACRODECK_HOST_POLL_MS}
	${If} $MacroDeckStopResult = ${MACRODECK_STOP_OK}
		Goto macrodeck_host_done_${MDHOOK}
	${EndIf}

	${If} $MacroDeckStopResult = ${MACRODECK_STOP_KILL_REFUSED}
		StrCpy $0 "Windows refused to end ${MACRODECK_HOST_BINARY}."
	${ElseIf} $MacroDeckStopResult = ${MACRODECK_STOP_LOCKED}
		StrCpy $0 "$INSTDIR\host\$MacroDeckLockedFile is still in use by another process."
	${Else}
		StrCpy $0 "${MACRODECK_HOST_BINARY} is still running."
	${EndIf}
	StrCpy $0 "$0$\n$\nQuit ${PRODUCTNAME} and end ${MACRODECK_HOST_BINARY} in Task Manager, then \
choose Retry. Nothing has been changed yet, so Cancel is safe."

	${If} ${Silent}
		; Mirrors the template's CheckIfAppIsRunning: a silent run has nowhere to prompt, so
		; report on the parent console and abort before a single file is written.
		System::Call 'kernel32::AttachConsole(i -1)i.r1'
		${If} $1 <> 0
			System::Call 'kernel32::GetStdHandle(i -11)i.r1'
			System::Call 'kernel32::SetConsoleTextAttribute(i r1, i 0x0004)'
			FileWrite $1 "$0$\n"
		${EndIf}
		Abort
	${EndIf}
	${If} $PassiveMode = 1
		; tauri-plugin-updater runs the installer with /P /R. The window is visible but must
		; never wait for input, so abort with the message instead of prompting.
		Abort "$0"
	${EndIf}
	MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$0" IDRETRY macrodeck_host_attempt_${MDHOOK}
	Abort "$0"

macrodeck_host_done_${MDHOOK}:
	Pop $1
	Pop $0

	!undef MACRODECK_HOST_SCOPE
	!insertmacro MacroDeckReleaseHostBinary
	!undef MDHOOK
!macroend

; Creates or removes the inbound Windows Firewall rule for the .NET host's public listener
; (issue #345), elevated through firewall.nsh. NSIS_HOOK_POSTINSTALL and NSIS_HOOK_PREUNINSTALL
; both insert this, so its hand-written labels need the same per-insertion uniquing as
; MacroDeckStopProcesses's.
;
; No Abort on any path: a firewall rule is a convenience, and an install that failed because
; someone clicked "No" on a UAC prompt would be strictly worse than the prompt this feature
; exists to remove.
!macro MacroDeckConfigureFirewall Action
	!define MDFWHOOK ${__LINE__}

	!insertmacro MacroDeckSelectHostBinary
	InitPluginsDir

	; The UAC prompt itself names "Windows Command Processor", not ${PRODUCTNAME} - netsh runs
	; behind cmd.exe /D /S /C, and Windows has no way to relabel that dialog. This is what
	; actually tells the user what they are being asked to elevate and why.
	!if "${Action}" == "add"
		MessageBox MB_OKCANCEL|MB_ICONINFORMATION \
			"${PRODUCTNAME} needs an inbound Windows Firewall rule so phones and other devices \
on your network can reach it.$\n$\nWindows will ask for administrator permission for this one \
step. Choose Cancel to skip it - ${PRODUCTNAME} still works, and Windows will simply ask you to \
allow it the first time you start it." \
			IDOK macrodeck_firewall_confirmed_${MDFWHOOK}
		DetailPrint "Skipping the Windows Firewall rule (user declined)."
		Goto macrodeck_firewall_done_${MDFWHOOK}
	!else
		MessageBox MB_OKCANCEL|MB_ICONINFORMATION \
			"Remove the Windows Firewall rule for ${PRODUCTNAME}?$\n$\nWindows will ask for \
administrator permission. Choose Cancel to leave it - it has no effect once ${PRODUCTNAME} is \
uninstalled." \
			IDOK macrodeck_firewall_confirmed_${MDFWHOOK}
		DetailPrint "Skipping the Windows Firewall rule removal (user declined)."
		Goto macrodeck_firewall_done_${MDFWHOOK}
	!endif

macrodeck_firewall_confirmed_${MDFWHOOK}:
	!insertmacro MacroDeckBuildFirewallCommand "${Action}" "${PRODUCTNAME}" "$INSTDIR\host\${MACRODECK_HOST_BINARY}" "$PLUGINSDIR\fw.ok"
	!insertmacro MacroDeckRunFirewallCommand "$PLUGINSDIR\fw.ok"

	${If} $MacroDeckFirewallResult = ${MACRODECK_FIREWALL_OK}
		DetailPrint "Windows Firewall rule for ${PRODUCTNAME} updated."
	${ElseIf} $MacroDeckFirewallResult = ${MACRODECK_FIREWALL_LAUNCH_FAILED}
		DetailPrint "Skipping the Windows Firewall rule (administrator permission was not granted)."
	${Else}
		DetailPrint "Windows Firewall rule for ${PRODUCTNAME} could not be updated."
	${EndIf}

macrodeck_firewall_done_${MDFWHOOK}:
	!insertmacro MacroDeckReleaseHostBinary
	!undef MDFWHOOK
!macroend

!macro NSIS_HOOK_PREINSTALL
	!insertmacro MacroDeckStopProcesses
!macroend

; POSTINSTALL rather than PREINSTALL: the rule targets $INSTDIR\host\<host>.exe, which only
; exists once the install section's resource copy has run - PREINSTALL fires before that.
!macro NSIS_HOOK_POSTINSTALL
	${If} ${Silent}
	${OrIf} $PassiveMode = 1
	${OrIf} $UpdateMode = 1
		; Not just "nowhere to prompt": GitHub's Windows runners are elevated, so without this
		; skip a /S install in mini-upgrade.nsi would start creating a real "Macro Deck" firewall
		; rule on the CI machine.
		DetailPrint "Skipping the Windows Firewall rule (unattended install)."
	${Else}
		!insertmacro MacroDeckConfigureFirewall "add"
	${EndIf}
!macroend

; The uninstall pass of an in-place upgrade runs before the install pass, so both need to stop
; the host. Macros expand into whatever section they land in, so no un. variant is needed.
!macro NSIS_HOOK_PREUNINSTALL
	!insertmacro MacroDeckStopProcesses
	; The template appends /UPDATE and /P to a driven uninstaller only when the installer itself
	; got them, and always appends _?=, so the $EXEDIR check below is what actually catches an
	; in-place reinstall; $UpdateMode here only covers an updater-driven one. The exception is a
	; migration from the old Wix installer, which runs the Wix uninstaller with none of the three
	; - unreachable for Macro Deck, which has never shipped an MSI.
	${If} ${Silent}
	${OrIf} $PassiveMode = 1
	${OrIf} $UpdateMode = 1
		DetailPrint "Skipping the Windows Firewall rule (unattended uninstall)."
	${ElseIf} $EXEDIR == $INSTDIR
		; $EXEDIR vs $INSTDIR is how "an installer is driving this uninstall" is detected. NSIS's
		; exehead strips _?= from $CMDLINE before any script code runs, so ${GetParameters} can
		; never see it. Without _?= the uninstaller copies itself to %TEMP% and relaunches, so
		; $EXEDIR != $INSTDIR; the Tauri template always passes _?=$INSTDIR when it drives the
		; old uninstaller for an in-place reinstall, and then it runs in place. This keeps a
		; manual "uninstall first, then install" re-run to one UAC prompt instead of two.
		DetailPrint "Skipping the Windows Firewall rule (reinstall in progress)."
	${Else}
		!insertmacro MacroDeckConfigureFirewall "remove"
	${EndIf}
!macroend

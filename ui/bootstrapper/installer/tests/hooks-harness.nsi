; Compiles the real hooks.nsh the way the Tauri NSIS template does.
;
; The ordering below is the point of this harness and must not be "tidied up": the template
; !includes the hooks file (installer.nsi line 35) BEFORE it defines PRODUCTNAME, INSTALLMODE and
; MAINBINARYNAME, and before utils.nsh's CheckIfAppIsRunning is available to insert. Issue #131
; regressed precisely because a top-level !if compared against a PRODUCTNAME that did not exist
; yet, silently selecting the Development host binary in every release installer. A harness that
; defined those symbols first would compile happily and prove nothing.
;
; Run by installer/tests/Invoke-InstallerTests.ps1. Compiling it is most of the value - it catches
; duplicate labels across the two hook insertions, an unbalanced Push/Pop, a broken ${__FILEDIR__}
; include and an AllowSkipFiles typo. Running it on a machine with no host process must exit 0.

Unicode true
Name "Macro Deck installer hook harness"
OutFile "${OUTFILE}"
InstallDir "$PLUGINSDIR\macro-deck-hooks"
RequestExecutionLevel user
SilentInstall silent
; A stray manual run of this harness's uninstaller (it is never invoked by the driver, but it is
; still compiled and left on disk) must not be able to pop a real UAC prompt.
SilentUnInstall silent
ShowInstDetails nevershow

!addplugindir /x86-unicode "${PLUGINSDIR_IN}"

!include "..\hooks.nsh"

; Everything the template provides only after the include.
!define PRODUCTNAME "Macro Deck"
!define MAINBINARYNAME "MacroDeck"
!define INSTALLMODE "currentUser"
Var PassiveMode
; hooks.nsh's NSIS_HOOK_POSTINSTALL/PREUNINSTALL read $UpdateMode too, so the harness has to
; declare it or the hook insertions further down no longer compile.
Var UpdateMode

; utils.nsh's macro stops the shell. The release build compiles the real one; here it only has to
; exist so the hook can insert it.
;
; It is also the only place the issue #131 guard can live: hooks.nsh evaluates the channel
; selector a few lines above its !insertmacro CheckIfAppIsRunning and !undefs the result at the
; end of the same macro, so this stub's body is the one point in the expansion where
; MACRODECK_HOST_BINARY exists. Asserting here fails the compile - for both the install and the
; uninstall insertion - the moment a release build would hunt the Development host.
;
; Not checked by preprocessing the harness instead: makensis only builds its plugin database
; when it actually generates an installer, so under /PPO !addplugindir is echoed rather than
; applied and the first nsis_tauri_utils:: call aborts the run. No script that calls a plugin
; can be preprocessed.
!macro CheckIfAppIsRunning executableName productName
	!if "${MACRODECK_HOST_BINARY}" != "MacroDeckHost.exe"
		!error "issue #131 regression: a release build selected ${MACRODECK_HOST_BINARY}"
	!endif
!macroend

; Guards the harness's own configuration: the assert above only means anything while PRODUCTNAME
; is the release product name.
!macro AssertReleaseHostSelected
	!if "${PRODUCTNAME}" != "Macro Deck"
		!error "harness misconfigured: PRODUCTNAME must be the release product name"
	!endif
!macroend
!insertmacro AssertReleaseHostSelected

Section Install
	SetOutPath $INSTDIR
	!insertmacro NSIS_HOOK_PREINSTALL
	WriteUninstaller "$INSTDIR\uninstall.exe"
	; Compile proof for the new hook: duplicate labels and an unbalanced Push/Pop across
	; MacroDeckConfigureFirewall's expansion would break here. SilentInstall above means ${Silent}
	; is true at runtime, so this never pops a real dialog or touches the real firewall - it only
	; proves the macro expansion is well-formed.
	!insertmacro NSIS_HOOK_POSTINSTALL
SectionEnd

; Compile proof that the macros stay usable in an uninstall section: NSIS's un. prefixing applies
; to functions, so this breaks the moment someone reaches for a Function in host-lock.nsh.
Section Uninstall
	!insertmacro NSIS_HOOK_PREUNINSTALL
SectionEnd

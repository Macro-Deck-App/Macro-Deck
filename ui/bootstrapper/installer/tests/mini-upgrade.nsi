; End-to-end in-place upgrade harness. Invoke-InstallerTests.ps1 compiles this twice - once with
; /DVERSION=1, once with /DVERSION=2 - installs v1 silently to a scratch dir, starts a fake host
; process out of its host\ directory, then runs v2 silently over the same install dir. If the
; pre-install hook did not really stop the host, v2 fails to overwrite host\version.txt exactly
; the way issue #131 failed to overwrite host\MacroDeckHost.exe.
;
; Reproduces the Tauri NSIS template's define ordering EXACTLY as hooks-harness.nsi does: ..\
; hooks.nsh is !include'd BEFORE PRODUCTNAME, MAINBINARYNAME, INSTALLMODE and PassiveMode exist,
; because that ordering is the entire issue #131 regression - a harness that defined those first
; would compile happily and prove nothing here either.
;
; hooks.nsh hardcodes the host binary name to MacroDeckHost.exe for PRODUCTNAME "Macro Deck", so
; the driver's fake host process for THIS harness must be named MacroDeckHost.exe. That is
; different from host-lock-harness.nsi, where the process name is a runtime parameter and
; MacroDeckHostTest.exe is used instead - do not mix the two up when editing the driver.
;
; This only approximates a real upgrade: it never exercises the template's actual
; PageLeaveReinstall -> old-uninstaller -> install-section sequence, and it installs unsigned,
; locally-built artifacts rather than a real release. Upgrading from a real published beta
; remains a manual release check.

Unicode true
Name "Macro Deck mini upgrade harness"
OutFile "${OUTFILE}"
; Never actually used: the driver always passes /D= to target its scratch dir; this default only
; keeps a manual "just run it" invocation out of Program Files.
InstallDir "$TEMP\MacroDeckMiniUpgradeHarness"
RequestExecutionLevel user

; Unconditionally silent, like the other two harnesses - CI has nowhere to click through a
; wizard. /S is still accepted (and is what the driver passes) for parity with how a real
; unattended install is invoked; NSIS ignores the redundant switch.
SilentInstall silent
; A stray manual run of this harness's uninstaller must not be able to pop a real UAC prompt.
SilentUnInstall silent
ShowInstDetails nevershow

!addplugindir /x86-unicode "${PLUGINSDIR_IN}"

!include "..\hooks.nsh"

; Everything the template provides only after the include - see hooks-harness.nsi for why the
; ordering above this line is load-bearing.
!define PRODUCTNAME "Macro Deck"
!define MAINBINARYNAME "MacroDeck"
!define INSTALLMODE "currentUser"
Var PassiveMode
; hooks.nsh's NSIS_HOOK_POSTINSTALL/PREUNINSTALL read $UpdateMode too, so the harness has to
; declare it or the hook insertions further down no longer compile.
Var UpdateMode

; utils.nsh's macro stops the shell; here it only has to exist so the hook can insert it.
!macro CheckIfAppIsRunning executableName productName
!macroend

!ifndef VERSION
!error "mini-upgrade.nsi must be compiled with /DVERSION=1 or /DVERSION=2"
!endif

Section Install
	SetOutPath $INSTDIR
	!insertmacro NSIS_HOOK_PREINSTALL

	SetOutPath "$INSTDIR\host"
	FileOpen $0 "$INSTDIR\host\version.txt" w
	FileWrite $0 "${VERSION}"
	FileClose $0
	; A second, real payload file (not just a FileWrite'd one) so the upgrade proves it can
	; replace an arbitrary file in host\, not only the one this script happens to write itself.
	File "fixtures\payload.bin"

	WriteUninstaller "$INSTDIR\uninstall.exe"
	; The driver always runs this installer with /S, so ${Silent} is true and this hook only ever
	; DetailPrints and skips - InPlaceUpgrade below keeps working unchanged and never touches the
	; real Windows Firewall. This is here only as the same duplicate-label / Push-Pop compile
	; proof hooks-harness.nsi gets.
	!insertmacro NSIS_HOOK_POSTINSTALL
SectionEnd

Section Uninstall
	!insertmacro NSIS_HOOK_PREUNINSTALL
SectionEnd

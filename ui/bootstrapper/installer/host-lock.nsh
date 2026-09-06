; Stopping a Windows process and proving it released the files in a directory.
;
; Deliberately independent of the Tauri NSIS template - no ${PRODUCTNAME}, ${MAINBINARYNAME},
; ${INSTALLMODE}, $PassiveMode, CheckIfAppIsRunning, MessageBox or Abort - so
; installer/tests/host-lock-harness.nsi can compile it on its own and CI can run the kill/verify
; loop against real processes. installer_config.rs asserts that independence.
;
; Results are globals rather than stack values: these macros nest, and the Exch juggling a stack
; result needs is exactly what regresses unnoticed. Callers must not pass $0-$2 as arguments;
; they are scratch and are restored.

!ifndef MACRODECK_HOST_LOCK_NSH
!define MACRODECK_HOST_LOCK_NSH

!include LogicLib.nsh

Var MacroDeckLockedFile
Var MacroDeckKillStatus
Var MacroDeckStopResult
Var MacroDeckStopElapsed

!define MACRODECK_STOP_OK 0
!define MACRODECK_STOP_RUNNING 1
!define MACRODECK_STOP_LOCKED 2
!define MACRODECK_STOP_KILL_REFUSED 3

; Sets $MacroDeckLockedFile to the first top-level entry of Directory that cannot be opened for
; writing, or "" when every one of them can. Top level only: the host directory also carries the
; Angular wwwroot tree, thousands of files that are never locked. A directory that does not exist
; - a first install - counts as writable.
!macro MacroDeckProbeDirectoryWritable Directory
	Push $0
	Push $1
	Push $2

	StrCpy $MacroDeckLockedFile ""
	${If} ${FileExists} "${Directory}\*.*"
		FindFirst $0 $1 "${Directory}\*"
		${DoWhile} $1 != ""
			${If} $1 != "."
			${AndIf} $1 != ".."
			${AndIfNot} ${FileExists} "${Directory}\$1\*.*"
				; "a" opens for read/write with the contents preserved; "w" would truncate the
				; very files the installer is about to replace. A running image file is held
				; without FILE_SHARE_WRITE, so this fails for exactly as long as the process
				; that owns it is still being torn down.
				FileOpen $2 "${Directory}\$1" a
				${If} $2 == ""
					StrCpy $MacroDeckLockedFile $1
					${ExitDo}
				${EndIf}
				FileClose $2
			${EndIf}
			FindNext $0 $1
		${Loop}
		FindClose $0
	${EndIf}

	Pop $2
	Pop $1
	Pop $0
!macroend

; Terminates every ProcessName - CurrentUserOnly "1" restricts it to the installing user - and
; waits until the process is gone AND Directory is writable again.
;
; TerminateProcess returns as soon as teardown is initiated; the kernel releases the image file
; afterwards, which is why a fixed sleep was never a guarantee (issue #131). The kill is repeated
; every pass because a shell that is still exiting can respawn the host.
;
; The kill result is recorded but never trusted on its own: KillProcessCurrentUser silently skips
; processes whose token it cannot read and then reports "nothing matched". The file probe decides.
!macro MacroDeckStopProcessAndWait ProcessName Directory CurrentUserOnly TimeoutMs PollMs
	Push $0
	Push $1
	Push $2

	StrCpy $1 0
	StrCpy $2 0
	StrCpy $MacroDeckStopResult ${MACRODECK_STOP_RUNNING}
	StrCpy $MacroDeckLockedFile ""

	${Do}
		!if "${CurrentUserOnly}" == "1"
			nsis_tauri_utils::KillProcessCurrentUser "${ProcessName}"
		!else
			nsis_tauri_utils::KillProcess "${ProcessName}"
		!endif
		; 0 = every match terminated, 1 = a match survived, 2 = nothing matched.
		Pop $0
		StrCpy $MacroDeckKillStatus $0
		${If} $0 = 1
			StrCpy $2 1
		${EndIf}

		!if "${CurrentUserOnly}" == "1"
			nsis_tauri_utils::FindProcessCurrentUser "${ProcessName}"
		!else
			nsis_tauri_utils::FindProcess "${ProcessName}"
		!endif
		; 0 = at least one match, 1 = none.
		Pop $0

		${If} $0 = 0
			StrCpy $MacroDeckStopResult ${MACRODECK_STOP_RUNNING}
		${Else}
			!insertmacro MacroDeckProbeDirectoryWritable "${Directory}"
			${If} $MacroDeckLockedFile == ""
				StrCpy $MacroDeckStopResult ${MACRODECK_STOP_OK}
				${ExitDo}
			${EndIf}
			StrCpy $MacroDeckStopResult ${MACRODECK_STOP_LOCKED}
		${EndIf}

		${If} $1 >= ${TimeoutMs}
			${ExitDo}
		${EndIf}
		Sleep ${PollMs}
		IntOp $1 $1 + ${PollMs}
	${Loop}

	; A refused kill is the more useful thing to report when the wait ran out anyway.
	${If} $MacroDeckStopResult <> ${MACRODECK_STOP_OK}
	${AndIf} $2 = 1
		StrCpy $MacroDeckStopResult ${MACRODECK_STOP_KILL_REFUSED}
	${EndIf}
	StrCpy $MacroDeckStopElapsed $1

	Pop $2
	Pop $1
	Pop $0
!macroend

!endif ; MACRODECK_HOST_LOCK_NSH

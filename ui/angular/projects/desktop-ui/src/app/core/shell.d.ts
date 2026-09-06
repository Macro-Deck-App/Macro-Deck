export {};

// Bridge injected by the Macro Deck Bootstrapper (see ui/bootstrapper/src/shell-bridge.js) before any
// page script runs. Absent in a plain browser; every call site must feature-detect and fall back.
declare global {
  interface Window {
    macroDeckShell?: {
      kind: 'tauri';
      getHostPort: () => Promise<number | null>;
      getShellInfo: () => Promise<{
        shellVersion: string;
        tauriVersion: string;
        webviewVersion: string | null;
      }>;
      getCursorPosition?: () => Promise<ShellCursorPosition | null>;
      openExternal: (url: string) => Promise<boolean>;
      showOpenDialog: (options?: {
        directory?: boolean;
        extensions?: string[];
      }) => Promise<string | null>;
      saveFile?: (options: {
        fileName: string;
        extensions: string[];
        data: ArrayBuffer;
      }) => Promise<ShellSaveFileResult>;
      onFileDrop?: (callback: (event: ShellFileDropEvent) => void) => Promise<() => void>;
      onFileOpen?: (callback: () => void) => Promise<() => void>;
      takeOpenedFiles?: () => Promise<string[]>;
      onMenuAction?: (callback: (event: ShellMenuActionEvent) => void) => Promise<() => void>;
      takeMenuAction?: () => Promise<string | null>;
      setHotkeyCapture?: (active: boolean) => Promise<void>;
      checkForUpdate?: () => Promise<ShellUpdateStatus>;
      installUpdate?: () => Promise<void>;
      onUpdateProgress?: (callback: (progress: ShellUpdateProgress) => void) => Promise<() => void>;
      getUpdateChannel?: () => Promise<ShellUpdateChannelStatus>;
      setUpdateChannel?: (channel: 'stable' | 'beta') => Promise<ShellUpdateChannelStatus>;
      getUpdateMode?: () => Promise<ShellUpdateModeStatus>;
      setUpdateMode?: (mode: 'off' | 'notifyOnly' | 'automatic') => Promise<ShellUpdateModeStatus>;
      getUpdateState?: () => Promise<ShellUpdateState>;
      requestUpdateCheck?: () => Promise<void>;
      cancelUpdateDownload?: () => Promise<void>;
      onUpdateState?: (callback: (state: ShellUpdateState) => void) => Promise<() => void>;
      getHideDockIcon?: () => Promise<ShellDockIconStatus>;
      setHideDockIcon?: (enabled: boolean) => Promise<ShellDockIconStatus>;
    };
  }

  interface ShellCursorPosition {
    x: number;
    y: number;
  }

  interface ShellSaveFileResult {
    saved: boolean;
    canceled: boolean;
    path: string | null;
    error: string | null;
  }

  interface ShellFileDropEvent {
    kind: 'enter' | 'over' | 'drop' | 'leave';
    paths: { path: string; directory: boolean }[];
    x: number;
    y: number;
  }

  interface ShellMenuActionEvent {
    action: 'settings';
  }

  interface ShellUpdateProgress {
    downloaded: number;
    total: number | null;
    percent: number | null;
  }

  interface ShellUpdateStatus {
    supported: boolean;
    currentVersion: string;
    available: boolean;
    version: string | null;
    notes: string | null;
    error: string | null;
    installStrategy: 'inApp' | 'externalDownload';
    downloadUrl: string;
    publishedAt: string | null;
    channel: 'stable' | 'beta';
    betaInstalled: boolean;
    partialCheck: string | null;
  }

  interface ShellDockIconStatus {
    supported: boolean;
    enabled: boolean;
  }

  interface ShellUpdateChannelStatus {
    channel: 'stable' | 'beta';
    betaEnabled: boolean;
  }

  interface ShellUpdateModeStatus {
    mode: 'off' | 'notifyOnly' | 'automatic';
    automaticSupported: boolean;
  }

  interface ShellUpdateState {
    phase:
      | 'unsupported'
      | 'idle'
      | 'checking'
      | 'upToDate'
      | 'available'
      | 'downloading'
      | 'downloaded'
      | 'installing'
      | 'failed';
    supported: boolean;
    currentVersion: string;
    version: string | null;
    notes: string | null;
    publishedAt: string | null;
    channel: string | null;
    betaInstalled: boolean;
    installStrategy: 'inApp' | 'externalDownload';
    downloadUrl: string | null;
    partialCheck: string | null;
    error: string | null;
    progress: ShellUpdateProgress | null;
    lastCheckedAt: number | null;
  }
}

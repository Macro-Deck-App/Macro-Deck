// Typed view of the desktop shell bridge (window.macroDeckShell, injected by the Tauri bootstrapper
// before any page script runs). Absent in a plain browser, so every call site feature-detects.
export interface ShellOpenDialogOptions {
  directory?: boolean;
  extensions?: string[];
}

export interface ShellDroppedPath {
  path: string;
  directory: boolean;
}

export interface ShellFileDropEvent {
  kind: 'enter' | 'over' | 'drop' | 'leave';
  paths: ShellDroppedPath[];
  x: number;
  y: number;
}

export interface ShellMenuActionEvent {
  action: 'settings';
}

export interface ShellSaveFileOptions {
  fileName: string;
  extensions: string[];
  data: ArrayBuffer;
}

export interface ShellSaveFileResult {
  saved: boolean;
  canceled: boolean;
  path: string | null;
  error: string | null;
}

export interface ShellCursorPosition {
  x: number;
  y: number;
}

export interface ShellBridge {
  getCursorPosition?: () => Promise<ShellCursorPosition | null>;
  openExternal?: (url: string) => Promise<boolean>;
  showOpenDialog?: (options?: ShellOpenDialogOptions) => Promise<string | null>;
  onFileDrop?: (callback: (event: ShellFileDropEvent) => void) => Promise<() => void>;
  onFileOpen?: (callback: () => void) => Promise<() => void>;
  takeOpenedFiles?: () => Promise<string[]>;
  onMenuAction?: (callback: (event: ShellMenuActionEvent) => void) => Promise<() => void>;
  takeMenuAction?: () => Promise<string | null>;
  onHostStopping?: (callback: () => void) => Promise<() => void>;
  saveFile?: (options: ShellSaveFileOptions) => Promise<ShellSaveFileResult>;
  setHotkeyCapture?: (active: boolean) => Promise<void>;
}

export function shellBridge(): ShellBridge | undefined {
  return (window as { macroDeckShell?: ShellBridge }).macroDeckShell;
}

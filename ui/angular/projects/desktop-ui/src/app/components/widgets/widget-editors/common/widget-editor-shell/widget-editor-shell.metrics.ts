export const EDITOR_SIDEBAR_WIDTH = 300;

export const EDITOR_LAYOUT_GAP_REM = 1.25;

export const EDITOR_MAIN_MIN_WIDTH_REM = 36;

export function isEditorCompact(availableWidth: number, rootFontSizePx: number): boolean {
  return availableWidth - EDITOR_SIDEBAR_WIDTH
    < (EDITOR_MAIN_MIN_WIDTH_REM + EDITOR_LAYOUT_GAP_REM) * rootFontSizePx;
}

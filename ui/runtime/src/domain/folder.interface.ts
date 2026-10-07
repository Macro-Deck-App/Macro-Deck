import { GridWidget } from './widget.interface';

export interface Folder {
  id: string;
  name: string;
  parentId: string | null;
  order: number;
  isExpanded: boolean;
  isDefault: boolean;
  cols: number | null;
  rows: number | null;
  background: string;
  backgroundSource?: string;
  spacing: number | null;
  borderRadius: number | null;
  emptyCellStyle?: EmptyCellStyle | null;
  viewId: string;
  viewConfiguration: string | null;
  widgets: GridWidget[];
}

export type EmptyCellStyle = 'visible' | 'transparent';

export const DEFAULT_EMPTY_CELL_STYLE: EmptyCellStyle = 'visible';

export function isEmptyCellStyle(value: unknown): value is EmptyCellStyle {
  return value === 'visible' || value === 'transparent';
}

export function emptyCellStyleFromWire(value: string | null | undefined): EmptyCellStyle | null {
  const normalized = value?.toLowerCase();
  return isEmptyCellStyle(normalized) ? normalized : null;
}

export const WIDGET_GRID_VIEW_ID = 'macrodeck.widget-grid';

export function isWidgetGridView(viewId: string | null | undefined): boolean {
  return !viewId || viewId === WIDGET_GRID_VIEW_ID;
}

export type FolderDropPosition = 'before' | 'after' | 'inside';

export type FolderMoveDirection = 'up' | 'down' | 'into' | 'out';

export interface FolderMoveRequest {
  folderId: string;
  targetId: string;
  position: FolderDropPosition;
}

export interface FolderDragState {
  isDragging: boolean;
  folderId: string | null;
  targetFolderId: string | null;
  dropPosition: FolderDropPosition | null;
}

export interface FolderContextMenu {
  isOpen: boolean;
  folderId: string | null;
  x: number;
  y: number;
}

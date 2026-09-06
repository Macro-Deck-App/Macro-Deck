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
  spacing: number | null;
  borderRadius: number | null;
  viewId: string;
  viewConfiguration: string | null;
  widgets: GridWidget[];
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

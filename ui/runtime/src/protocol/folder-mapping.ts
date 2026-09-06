import { Folder, WIDGET_GRID_VIEW_ID } from '../domain/folder.interface';
import { GridWidget } from '../domain/widget.interface';
import { parseWidgetData } from '../domain/widget-serialization';
import { IpcFolder } from './messages/folder';
import { IpcWidget } from './messages/widget';

const LEGACY_DEFAULT_BACKGROUND = 'rgba(18, 18, 18, 0.6)';
const DEFAULT_BACKGROUND = '';

export function folderFromWire(ipc: IpcFolder): Folder {
  return {
    id: ipc.id,
    name: ipc.name,
    parentId: ipc.parentId ?? null,
    order: ipc.order,
    isExpanded: true,
    isDefault: ipc.isDefault ?? false,
    // null rather than a number: the folder inherits from its parent, then from the profile, and a
    // client that substituted its own default here would override an inheritance it cannot see.
    cols: ipc.columns ?? null,
    rows: ipc.rows ?? null,
    background: ipc.backgroundColor === LEGACY_DEFAULT_BACKGROUND
      ? DEFAULT_BACKGROUND
      : (ipc.backgroundColor ?? DEFAULT_BACKGROUND),
    spacing: ipc.widgetSpacing ?? null,
    borderRadius: ipc.widgetBorderRadius ?? null,
    viewId: ipc.viewId || WIDGET_GRID_VIEW_ID,
    viewConfiguration: ipc.viewConfiguration ?? null,
    widgets: (ipc.widgets ?? []).map(widget => widgetFromWire(widget, ipc.id)),
  };
}

export function widgetFromWire(ipc: IpcWidget, folderId: string): GridWidget {
  return {
    id: ipc.id,
    folderId,
    x: ipc.positionX,
    y: ipc.positionY,
    w: ipc.width,
    h: ipc.height,
    type: ipc.type,
    data: parseWidgetData(ipc.type, ipc.data),
    isPinned: ipc.isPinned ?? false,
    pinScope: ipc.pinScope ?? 'Profile',
  };
}

export function foldersFromWire(ipc: readonly IpcFolder[] | undefined): Folder[] {
  return (ipc ?? []).map(folderFromWire);
}

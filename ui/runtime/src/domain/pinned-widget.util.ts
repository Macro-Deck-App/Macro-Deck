import { Folder } from './folder.interface';
import { GridWidget, PinScope } from './widget.interface';

function ancestorsAndSelf(folders: readonly Folder[], folderId: string): Set<string> {
  const byId = new Map(folders.map(f => [f.id, f]));
  const result = new Set<string>();
  let currentId: string | null = folderId;
  while (currentId !== null && !result.has(currentId)) {
    result.add(currentId);
    currentId = byId.get(currentId)?.parentId ?? null;
  }
  return result;
}

function reaches(targetAncestors: ReadonlySet<string>, homeId: string, scope: PinScope | undefined): boolean {
  if ((scope ?? 'Profile') === 'Profile') {
    return true;
  }
  return targetAncestors.has(homeId);
}

export function foreignPinnedWidgets(folders: readonly Folder[], folderId: string | null): GridWidget[] {
  if (folderId === null) {
    return folders.flatMap(folder => folder.widgets.filter(widget => widget.isPinned));
  }

  const targetAncestors = ancestorsAndSelf(folders, folderId);

  return folders
    .filter(folder => folder.id !== folderId)
    .flatMap(folder => folder.widgets.filter(widget =>
      widget.isPinned && reaches(targetAncestors, folder.id, widget.pinScope)));
}

export function collectDisplayedWidgets(folders: readonly Folder[], folderId: string | null): GridWidget[] {
  const folder = folders.find(f => f.id === folderId);
  if (!folder) {
    return [];
  }

  const foreign = foreignPinnedWidgets(folders, folder.id);
  return foreign.length > 0 ? [...folder.widgets, ...foreign] : folder.widgets;
}

import { ShellDroppedPath } from '../util/shell-bridge';
import { droppedPathExtension } from './icon-drop.util';

export type ArchiveDropKind = 'profile' | 'folder' | 'widgets' | 'iconPack' | 'plugin';

export const ARCHIVE_DROP_EXTENSIONS: Readonly<Record<ArchiveDropKind, string>> = {
  profile: 'macrodeckprofile',
  folder: 'macrodeckfolder',
  widgets: 'macrodeckwidget',
  iconPack: 'macrodeckiconpack',
  plugin: 'macrodeckplugin',
};

export function archiveDropKind(path: string): ArchiveDropKind | null {
  const extension = droppedPathExtension(path);
  const match = Object.entries(ARCHIVE_DROP_EXTENSIONS)
    .find(([, candidate]) => candidate === extension);
  return match ? (match[0] as ArchiveDropKind) : null;
}

export function acceptsArchiveDropPath(path: ShellDroppedPath, kinds: readonly ArchiveDropKind[]): boolean {
  if (path.directory) {
    return false;
  }

  const kind = archiveDropKind(path.path);
  return kind !== null && kinds.includes(kind);
}

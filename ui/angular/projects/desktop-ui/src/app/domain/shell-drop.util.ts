import { ShellDroppedPath } from '../util/shell-bridge';
import { ArchiveDropKind, acceptsArchiveDropPath, archiveDropKind } from './archive-drop.util';
import { droppedPathExtension } from './icon-drop.util';

export type ShellDropKind = ArchiveDropKind | 'application';

const APPLICATION_EXTENSIONS: readonly string[] = ['app', 'exe', 'lnk', 'url', 'sh', 'desktop'];

export function isApplicationDropPath(path: ShellDroppedPath): boolean {
  const extension = droppedPathExtension(path.path);
  if (path.directory) {
    return extension === 'app';
  }

  return extension === '' || APPLICATION_EXTENSIONS.includes(extension);
}

export function shellDropKind(path: ShellDroppedPath): ShellDropKind | null {
  return isApplicationDropPath(path) ? 'application' : archiveDropKind(path.path);
}

export function acceptsShellDropPath(path: ShellDroppedPath, kinds: readonly ShellDropKind[]): boolean {
  if (isApplicationDropPath(path)) {
    return kinds.includes('application');
  }

  return acceptsArchiveDropPath(path, kinds.filter((kind): kind is ArchiveDropKind => kind !== 'application'));
}

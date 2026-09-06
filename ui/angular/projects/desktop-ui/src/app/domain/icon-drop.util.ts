import { ShellDroppedPath } from '../util/shell-bridge';

export const ICON_DROP_EXTENSIONS: readonly string[] = [
  'png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'lottie',
  'ico', 'icns', 'exe', 'dll', 'lnk', 'url', 'desktop',
];

export function droppedPathExtension(path: string): string {
  const name = path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? '';
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot + 1).toLowerCase() : '';
}

export function acceptsIconDropPath(path: ShellDroppedPath): boolean {
  const extension = droppedPathExtension(path.path);
  if (path.directory) {
    return extension === 'app';
  }

  return ICON_DROP_EXTENSIONS.includes(extension);
}

export const ICON_PACK_ARCHIVE_EXTENSIONS: readonly string[] = [
  'macrodeckiconpack', 'streamdeckiconpack', 'tpi', 'zip',
];

export function isIconPackArchivePath(path: ShellDroppedPath): boolean {
  if (path.directory) {
    return false;
  }

  return ICON_PACK_ARCHIVE_EXTENSIONS.includes(droppedPathExtension(path.path));
}

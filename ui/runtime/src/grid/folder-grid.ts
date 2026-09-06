import { WIDGET_REFERENCE_BORDER_RADIUS, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';
import { DEFAULT_GRID_GEOMETRY } from './grid-metrics';

export interface ProfileGridDefaults {
  columns?: number;
  rows?: number;
  spacing?: number;
  borderRadius?: number;
}

export interface FolderGridSettings {
  cols: number | null;
  rows: number | null;
  spacing: number | null;
  borderRadius: number | null;
}

export interface ResolvedFolderGrid {
  cols: number;
  rows: number;
  spacing: number;
  borderRadius: number;
}

function inherited(
  chain: readonly (FolderGridSettings | null | undefined)[],
  pick: (folder: FolderGridSettings) => number | null,
  profile: number | undefined,
  fallback: number,
): number {
  for (let index = 0; index < chain.length; index++) {
    const folder = chain[index];
    if (!folder) continue;
    const stated = pick(folder);
    if (typeof stated === 'number') return stated;
  }
  if (typeof profile === 'number') return profile;
  return fallback;
}

export function resolveFolderGrid(
  folder: FolderGridSettings | null | undefined,
  profile?: ProfileGridDefaults,
  ancestors: readonly FolderGridSettings[] = [],
): ResolvedFolderGrid {
  const chain = [folder as FolderGridSettings | null | undefined].concat(ancestors);
  return {
    cols: inherited(chain, stated => stated.cols, profile?.columns, DEFAULT_GRID_GEOMETRY.cols),
    rows: inherited(chain, stated => stated.rows, profile?.rows, DEFAULT_GRID_GEOMETRY.rows),
    spacing: inherited(chain, stated => stated.spacing, profile?.spacing, WIDGET_REFERENCE_GAP),
    borderRadius: inherited(
      chain, stated => stated.borderRadius, profile?.borderRadius, WIDGET_REFERENCE_BORDER_RADIUS),
  };
}

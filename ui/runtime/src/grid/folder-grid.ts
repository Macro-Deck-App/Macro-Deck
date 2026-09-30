import { DEFAULT_EMPTY_CELL_STYLE, EmptyCellStyle, isEmptyCellStyle } from '../domain/folder.interface';
import { WIDGET_REFERENCE_BORDER_RADIUS, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';
import { DEFAULT_GRID_GEOMETRY } from './grid-metrics';

export interface ProfileGridDefaults {
  columns?: number;
  rows?: number;
  spacing?: number;
  borderRadius?: number;
  emptyCellStyle?: EmptyCellStyle | null;
  shadows?: boolean | null;
}

export interface FolderGridSettings {
  cols: number | null;
  rows: number | null;
  spacing: number | null;
  borderRadius: number | null;
  emptyCellStyle?: EmptyCellStyle | null;
}

export interface ResolvedFolderGrid {
  cols: number;
  rows: number;
  spacing: number;
  borderRadius: number;
  emptyCellStyle: EmptyCellStyle;
  shadows: boolean;
}

function inherited<T>(
  chain: readonly (FolderGridSettings | null | undefined)[],
  pick: (folder: FolderGridSettings) => unknown,
  profile: unknown,
  fallback: T,
  accepts: (value: unknown) => value is T,
): T {
  for (let index = 0; index < chain.length; index++) {
    const folder = chain[index];
    if (!folder) continue;
    const stated = pick(folder);
    if (accepts(stated)) return stated;
  }
  if (accepts(profile)) return profile;
  return fallback;
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number';
}

export function resolveFolderGrid(
  folder: FolderGridSettings | null | undefined,
  profile?: ProfileGridDefaults,
  ancestors: readonly FolderGridSettings[] = [],
): ResolvedFolderGrid {
  const chain = [folder as FolderGridSettings | null | undefined].concat(ancestors);
  return {
    cols: inherited(chain, stated => stated.cols, profile?.columns, DEFAULT_GRID_GEOMETRY.cols, isNumber),
    rows: inherited(chain, stated => stated.rows, profile?.rows, DEFAULT_GRID_GEOMETRY.rows, isNumber),
    spacing: inherited(chain, stated => stated.spacing, profile?.spacing, WIDGET_REFERENCE_GAP, isNumber),
    borderRadius: inherited(
      chain, stated => stated.borderRadius, profile?.borderRadius, WIDGET_REFERENCE_BORDER_RADIUS, isNumber),
    emptyCellStyle: inherited(
      chain, stated => stated.emptyCellStyle, profile?.emptyCellStyle, DEFAULT_EMPTY_CELL_STYLE, isEmptyCellStyle),
    shadows: !profile || profile.shadows !== false,
  };
}

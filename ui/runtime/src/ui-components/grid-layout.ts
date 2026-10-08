import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeNumber } from '../ui-framework/node-properties.util';
import { UiComponentProperties } from './component-properties';

export interface UiGridPlacement {
  child: UiNode;
  row: number;
  column: number;
  rowSpan: number;
  columnSpan: number;
}

export interface UiGridCell {
  child: UiNode;
  left: number;
  top: number;
  width: number;
  height: number;
}

export const GRID_MAX_COUNT = 64;

function count(node: UiNode, key: string): number {
  const value = nodeNumber(node, key);
  return value !== undefined && value >= 1 ? Math.min(GRID_MAX_COUNT, Math.floor(value)) : 1;
}

const TIE_EPSILON = 1e-6;

export function placeGridChildren(
  node: UiNode,
  chosenColumns?: number,
): { placements: UiGridPlacement[]; columns: number; rows: number } {
  const columns = chosenColumns ?? count(node, UiComponentProperties.Columns);
  const declaredRows = nodeNumber(node, UiComponentProperties.Rows);
  const rowLimit = chosenColumns === undefined && declaredRows !== undefined && declaredRows >= 0
    ? Math.min(GRID_MAX_COUNT, Math.floor(declaredRows))
    : null;

  const taken: boolean[][] = [];
  const isFree = (row: number, column: number, rowSpan: number, columnSpan: number) => {
    for (let r = row; r < row + rowSpan; r++) {
      for (let c = column; c < column + columnSpan; c++) {
        if (taken[r]?.[c]) return false;
      }
    }
    return true;
  };

  const placements: UiGridPlacement[] = [];
  let usedRows = 0;
  for (const child of node.children ?? []) {
    const columnSpan = Math.min(count(child, UiComponentProperties.ColumnSpan), columns);
    const rowSpan = count(child, UiComponentProperties.RowSpan);

    let placed: UiGridPlacement | null = null;
    for (let row = 0; placed === null && (rowLimit === null || row + rowSpan <= rowLimit); row++) {
      for (let column = 0; column + columnSpan <= columns; column++) {
        if (isFree(row, column, rowSpan, columnSpan)) {
          placed = { child, row, column, rowSpan, columnSpan };
          break;
        }
      }
    }
    if (placed === null) continue;

    for (let r = placed.row; r < placed.row + rowSpan; r++) {
      taken[r] = taken[r] ?? [];
      for (let c = placed.column; c < placed.column + columnSpan; c++) taken[r][c] = true;
    }
    usedRows = Math.max(usedRows, placed.row + rowSpan);
    placements.push(placed);
  }

  return { placements, columns, rows: rowLimit ?? usedRows };
}

function rowsFor(node: UiNode, columns: number): number {
  const children = node.children ?? [];
  const spans = children.some(child => count(child, UiComponentProperties.ColumnSpan) > 1
    || count(child, UiComponentProperties.RowSpan) > 1);
  return spans ? placeGridChildren(node, columns).rows : Math.ceil(children.length / columns);
}

export function chooseColumns(
  node: UiNode,
  width: number | null,
  height: number | null,
  padding: number,
  gap: number,
  minCell: number | undefined,
): number | undefined {
  if (minCell === undefined || !Number.isFinite(minCell) || minCell <= 0 || width === null) return undefined;

  const contentWidth = Math.max(0, width - 2 * padding);
  const contentHeight = height === null ? null : Math.max(0, height - 2 * padding);
  const limit = Math.min(GRID_MAX_COUNT, Math.max(1, (node.children ?? []).length));
  const columnEdge = (columns: number) => Math.max(0, (contentWidth - (columns - 1) * gap) / columns);

  if (contentHeight === null) {
    let chosen = 1;
    for (let columns = 2; columns <= limit; columns++) {
      if (columnEdge(columns) >= minCell - TIE_EPSILON) chosen = columns;
    }
    return chosen;
  }

  let chosen = 1;
  let bestEdge = -1;
  for (let columns = 1; columns <= limit; columns++) {
    const rows = rowsFor(node, columns);
    const rowEdge = rows === 0 ? 0 : Math.max(0, (contentHeight - (rows - 1) * gap) / rows);
    const edge = Math.min(columnEdge(columns), rowEdge);
    if (edge > bestEdge + TIE_EPSILON) {
      bestEdge = edge;
      chosen = columns;
    }
  }
  return chosen;
}

export function layoutGridCells(
  node: UiNode,
  width: number | null,
  height: number | null,
  padding: number,
  gap: number,
  chosenColumns?: number,
): UiGridCell[] {
  const { placements, columns, rows } = placeGridChildren(node, chosenColumns);
  const contentWidth = Math.max(0, (width ?? 0) - 2 * padding);
  const contentHeight = height === null ? null : Math.max(0, height - 2 * padding);
  const cellWidth = Math.max(0, (contentWidth - (columns - 1) * gap) / columns);
  const cellHeight = contentHeight === null
    ? cellWidth
    : rows === 0 ? 0 : Math.max(0, (contentHeight - (rows - 1) * gap) / rows);

  return placements.map(p => ({
    child: p.child,
    left: padding + p.column * (cellWidth + gap),
    top: padding + p.row * (cellHeight + gap),
    width: p.columnSpan * cellWidth + (p.columnSpan - 1) * gap,
    height: p.rowSpan * cellHeight + (p.rowSpan - 1) * gap,
  }));
}

export function gridIntrinsicHeight(
  node: UiNode,
  width: number,
  padding: number,
  gap: number,
  chosenColumns?: number,
): number {
  const { columns, rows } = placeGridChildren(node, chosenColumns);
  const cellWidth = Math.max(0, (width - 2 * padding - (columns - 1) * gap) / columns);
  return rows === 0 ? 2 * padding : 2 * padding + rows * cellWidth + (rows - 1) * gap;
}

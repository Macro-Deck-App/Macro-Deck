import {
  GridRect,
  cellFromPoint,
  computeCellDimensions,
} from './grid-layout.util';
import { CellDimensions, WIDGET_REFERENCE_CELL_SIZE, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';

export interface GridGeometry {
  cols: number;
  rows: number;
  spacing: number;
  outerMargin: number;
}

export const DEFAULT_GRID_GEOMETRY: GridGeometry = {
  cols: 5,
  rows: 3,
  spacing: WIDGET_REFERENCE_GAP,
  outerMargin: 16,
};

export function cellOffset(index: number, cellSize: number, gap: number, padding: number): number {
  return padding + index * (cellSize + gap);
}

export function spanSize(span: number, cellSize: number, gap: number): number {
  return span * cellSize + (span - 1) * gap;
}

export function contentScaleFor(cellWidth: number, cellHeight: number): number {
  const cell = Math.min(cellWidth, cellHeight);
  return cell > 0 ? cell / WIDGET_REFERENCE_CELL_SIZE : 1;
}

export class GridMetrics {
  private geometry: GridGeometry = DEFAULT_GRID_GEOMETRY;
  private solved: CellDimensions = { cellWidth: 0, cellHeight: 0, gap: 0, padding: 0 };
  private solvedWidth = 0;
  private solvedHeight = 0;
  private measuredFor: { width: number; height: number } | null = null;

  configure(geometry: Partial<GridGeometry>): void {
    this.geometry = { ...this.geometry, ...geometry };
    this.measuredFor = null;
  }

  measure(wrapperWidth: number, wrapperHeight: number): boolean {
    if (this.measuredFor?.width === wrapperWidth && this.measuredFor.height === wrapperHeight) {
      return false;
    }

    const { cols, rows, spacing, outerMargin } = this.geometry;
    this.solved = computeCellDimensions(
      wrapperWidth - 2 * outerMargin,
      wrapperHeight - 2 * outerMargin,
      cols,
      rows,
      spacing,
    );

    const { cellWidth, cellHeight, gap, padding } = this.solved;
    this.solvedWidth = spanSize(cols, cellWidth, gap) + 2 * padding;
    this.solvedHeight = spanSize(rows, cellHeight, gap) + 2 * padding;
    this.measuredFor = { width: wrapperWidth, height: wrapperHeight };
    return true;
  }

  get cell(): CellDimensions {
    return this.solved;
  }

  get gap(): number {
    return this.solved.gap;
  }

  get padding(): number {
    return this.solved.padding;
  }

  get width(): number {
    return this.solvedWidth;
  }

  get height(): number {
    return this.solvedHeight;
  }

  get cols(): number {
    return this.geometry.cols;
  }

  get rows(): number {
    return this.geometry.rows;
  }

  get outerMargin(): number {
    return this.geometry.outerMargin;
  }

  get cellCount(): number {
    return this.geometry.cols * this.geometry.rows;
  }

  get contentScale(): number {
    return contentScaleFor(this.solved.cellWidth, this.solved.cellHeight);
  }

  cellRect(index: number): GridRect {
    return { x: index % this.geometry.cols, y: Math.floor(index / this.geometry.cols), w: 1, h: 1 };
  }

  cellIndex(x: number, y: number): number {
    return y * this.geometry.cols + x;
  }

  left(rect: GridRect): number {
    return cellOffset(rect.x, this.solved.cellWidth, this.solved.gap, this.solved.padding);
  }

  top(rect: GridRect): number {
    return cellOffset(rect.y, this.solved.cellHeight, this.solved.gap, this.solved.padding);
  }

  widthOf(rect: GridRect): number {
    return spanSize(rect.w, this.solved.cellWidth, this.solved.gap);
  }

  heightOf(rect: GridRect): number {
    return spanSize(rect.h, this.solved.cellHeight, this.solved.gap);
  }

  cellAt(offsetX: number, offsetY: number): { x: number; y: number } | null {
    return cellFromPoint(offsetX, offsetY, this.solved, this.geometry.cols, this.geometry.rows);
  }
}

export function isCellOccupied(rects: readonly GridRect[], cellX: number, cellY: number): boolean {
  return rects.some(rect =>
    cellX >= rect.x &&
    cellX < rect.x + rect.w &&
    cellY >= rect.y &&
    cellY < rect.y + rect.h
  );
}

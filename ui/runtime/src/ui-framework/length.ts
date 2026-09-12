import { UiNode } from './ui-node.interface';
import { nodeRaw } from './node-properties.util';

export const UI_COMPONENT_CELL = 120;

export interface UiLength {
  basis: number;
  maxOfCross?: number;
  maxOfCell?: number;
}

function fraction(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

export function nodeLength(
  node: UiNode | null | undefined,
  key: string,
): UiLength | undefined {
  return asUiLength(nodeRaw(node, key));
}

export function asUiLength(raw: unknown): UiLength | undefined {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;

  const candidate = raw as { basis?: unknown; maxOfCross?: unknown; maxOfCell?: unknown };
  if (typeof candidate.basis !== 'number' || !Number.isFinite(candidate.basis)) return undefined;

  const length: UiLength = { basis: candidate.basis };
  const maxOfCross = fraction(candidate.maxOfCross);
  if (maxOfCross !== undefined) length.maxOfCross = maxOfCross;
  const maxOfCell = fraction(candidate.maxOfCell);
  if (maxOfCell !== undefined) length.maxOfCell = maxOfCell;

  return length;
}

export function resolveLength(
  length: UiLength | undefined,
  basis: number,
  crossExtent: number | null,
): number | undefined {
  if (!length) return undefined;

  let resolved = length.basis * basis;

  if (length.maxOfCross !== undefined && crossExtent !== null && Number.isFinite(crossExtent)) {
    resolved = Math.min(resolved, length.maxOfCross * crossExtent);
  }

  if (length.maxOfCell !== undefined) {
    resolved = Math.min(resolved, length.maxOfCell * UI_COMPONENT_CELL);
  }

  return resolved;
}

const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

export function nodeHexColor(node: UiNode | null | undefined, key: string): string | undefined {
  return asHexColor(nodeRaw(node, key));
}

export function asHexColor(raw: unknown): string | undefined {
  return typeof raw === 'string' && HEX_COLOR.test(raw) ? raw : undefined;
}

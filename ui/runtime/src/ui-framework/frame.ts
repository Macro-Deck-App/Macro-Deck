import { UiComponentBox } from './layout';
import { asUiLength, resolveLength } from './length';
import { nodeRecord } from './node-properties.util';
import { UiNode } from './ui-node.interface';
import { UiComponentProperties } from '../ui-components/component-properties';

export interface UiResolvedFrame {
  box: UiComponentBox;
  minWidth?: number;
  maxWidth?: number;
  minHeight?: number;
  maxHeight?: number;
}

function clamp(value: number | null, min: number | undefined, max: number | undefined): number | null {
  if (value === null) return null;
  let clamped = value;
  if (max !== undefined) clamped = Math.min(clamped, max);
  if (min !== undefined) clamped = Math.max(clamped, min);
  return clamped;
}

export function resolveFrame(
  node: UiNode,
  box: UiComponentBox,
  basis: number,
  crossExtent: number | null,
): UiResolvedFrame {
  const frame = nodeRecord(node, UiComponentProperties.Frame) ?? {};
  const length = (key: string) => resolveLength(asUiLength(frame[key]), basis, crossExtent);

  const minWidth = length('minWidth');
  const maxWidth = length('maxWidth');
  const minHeight = length('minHeight');
  const maxHeight = length('maxHeight');
  let width = clamp(length('width') ?? box.width, minWidth, maxWidth);
  let height = clamp(length('height') ?? box.height, minHeight, maxHeight);

  const ratio = frame['aspectRatio'];
  if (typeof ratio === 'number' && Number.isFinite(ratio) && ratio > 0) {
    if (width !== null && height === null) {
      height = clamp(width / ratio, minHeight, maxHeight);
    } else if (height !== null && width === null) {
      width = clamp(height * ratio, minWidth, maxWidth);
    } else if (width !== null && height !== null) {
      if (width / height > ratio) width = height * ratio;
      else height = width / ratio;
    }
  }

  return { box: { width, height }, minWidth, maxWidth, minHeight, maxHeight };
}

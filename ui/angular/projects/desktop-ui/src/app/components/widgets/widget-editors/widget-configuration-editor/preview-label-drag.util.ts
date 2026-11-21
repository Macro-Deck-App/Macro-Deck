import { UiConfigProperties, UiNode } from '@macro-deck/runtime';

export interface LabelFields {
  picker: UiNode | null;
  x: UiNode | null;
  y: UiNode | null;
}

export interface Box {
  left: number;
  top: number;
  width: number;
  height: number;
}

export interface PreviewLabel {
  index: number;
  box: Box;
}

const PickerKey = 'labelPicker';
const XKey = 'labelX';
const YKey = 'labelY';
const HitSlop = 4;

function endsWithKey(id: string, key: string): boolean {
  return id === key || id.endsWith(`.${key}`);
}

// Only the picked label's form is in the tree, so there is at most one position field per axis.
export function findLabelFields(root: UiNode | null): LabelFields {
  const fields: LabelFields = { picker: null, x: null, y: null };
  const visit = (node: UiNode): void => {
    if (endsWithKey(node.id, PickerKey)) fields.picker ??= node;
    else if (endsWithKey(node.id, XKey)) fields.x ??= node;
    else if (endsWithKey(node.id, YKey)) fields.y ??= node;
    for (const child of node.children ?? []) visit(child);
  };
  if (root) visit(root);
  return fields;
}

export function pickedLabelIndex(fields: LabelFields): number | null {
  const value = fields.picker?.properties?.[UiConfigProperties.Value];
  const index = typeof value === 'string' ? Number.parseInt(value, 10) : Number.NaN;
  return Number.isInteger(index) ? index : null;
}

function boxOf(rect: DOMRect): Box {
  return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
}

function inkOf(element: Element): Box {
  const range = element.ownerDocument.createRange();
  range.selectNodeContents(element);
  return boxOf(range.getBoundingClientRect());
}

// A button whose labels all sit on presets draws one full-width label, so its ink is measured instead.
export function previewLabels(frame: HTMLElement): PreviewLabel[] {
  const labels: PreviewLabel[] = [];
  for (const pin of Array.from(frame.querySelectorAll('[data-node-id*="labelPin"]'))) {
    const match = /labelPin(Primary|\d+)$/.exec(pin.getAttribute('data-node-id') ?? '');
    if (!match) continue;
    labels.push({ index: match[1] === 'Primary' ? 0 : Number(match[1]) + 1, box: boxOf(pin.getBoundingClientRect()) });
  }
  if (labels.length === 0) {
    const flat = frame.querySelector('[data-node-id$="labelRow.labelBox.label"]');
    if (flat) labels.push({ index: 0, box: inkOf(flat) });
  }
  return labels.filter(label => label.box.width > 0 && label.box.height > 0);
}

export function labelAt(labels: readonly PreviewLabel[], x: number, y: number): PreviewLabel | null {
  for (let i = labels.length - 1; i >= 0; i--) {
    const box = labels[i].box;
    if (x >= box.left - HitSlop && x <= box.left + box.width + HitSlop
      && y >= box.top - HitSlop && y <= box.top + box.height + HitSlop) return labels[i];
  }
  return null;
}

export function labelArea(frame: HTMLElement): Box | null {
  const layer = frame.querySelector('[data-node-id$=".labels"]');
  if (layer) return boxOf(layer.getBoundingClientRect());

  const button = frame.querySelector('[data-node-id="actionButton"]');
  if (!(button instanceof HTMLElement)) return null;

  const rect = button.getBoundingClientRect();
  const style = getComputedStyle(button);
  const scaleX = button.offsetWidth > 0 ? rect.width / button.offsetWidth : 1;
  const scaleY = button.offsetHeight > 0 ? rect.height / button.offsetHeight : 1;
  const left = Number.parseFloat(style.paddingLeft) * scaleX;
  const right = Number.parseFloat(style.paddingRight) * scaleX;
  const top = Number.parseFloat(style.paddingTop) * scaleY;
  const bottom = Number.parseFloat(style.paddingBottom) * scaleY;
  return {
    left: rect.left + left, top: rect.top + top,
    width: rect.width - left - right, height: rect.height - top - bottom,
  };
}

// 0 is flush start and 100 flush end, the same scale the position fields use.
export function positionPercent(labelStart: number, areaStart: number, areaSize: number, labelSize: number): number | null {
  const free = areaSize - labelSize;
  if (!(free > 0)) return null;
  return Math.round(Math.min(1, Math.max(0, (labelStart - areaStart) / free)) * 100);
}

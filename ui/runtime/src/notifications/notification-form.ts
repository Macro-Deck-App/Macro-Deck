import { UiComponentEvents } from '../ui-components/component-events';
import { UiComponentProperties } from '../ui-components/component-properties';
import { UiComponents } from '../ui-components/ui-component-types';
import { UiNode, UiNodeEvent } from '../ui-framework/ui-node.interface';

export type NotificationDraft = Record<string, unknown>;

type FieldKind = 'text' | 'toggle' | 'slider' | 'segmented';

interface Field {
  id: string;
  kind: FieldKind;
  node: UiNode;
}

function kindOf(node: UiNode): FieldKind | null {
  switch (node.type) {
    case UiComponents.TextField:
      return 'text';
    case UiComponents.Toggle:
      return 'toggle';
    case UiComponents.Slider:
      return 'slider';
    case UiComponents.Segmented:
      return 'segmented';
    default:
      return null;
  }
}

function declaresValueEvent(node: UiNode): boolean {
  const events = node.properties?.[UiComponentProperties.Events];
  if (!Array.isArray(events)) return false;

  for (const name of events) {
    if (name === UiComponentEvents.Change || name === UiComponentEvents.Adjust) return true;
  }

  return false;
}

function collect(node: UiNode, into: Field[]): void {
  const kind = kindOf(node);
  if (kind !== null && declaresValueEvent(node)) into.push({ id: node.id, kind, node });

  for (const child of node.children ?? []) collect(child, into);
  if (node.fallback) collect(node.fallback, into);
}

function fieldsOf(tree: UiNode): Field[] {
  const fields: Field[] = [];
  collect(tree, fields);
  return fields;
}

function seedOf(field: Field): unknown {
  const props = field.node.properties ?? {};

  switch (field.kind) {
    case 'text': {
      const text = props[UiComponentProperties.Text];
      return typeof text === 'string' ? text : '';
    }
    case 'toggle':
      return props[UiComponentProperties.On] === true;
    case 'slider': {
      const level = props[UiComponentProperties.Level];
      return typeof level === 'number' ? level : 0;
    }
    default: {
      const selected = props[UiComponentProperties.Selected];
      return typeof selected === 'number' ? selected : null;
    }
  }
}

function acceptable(kind: FieldKind, value: unknown): boolean {
  switch (kind) {
    case 'text':
      return typeof value === 'string';
    case 'toggle':
      return typeof value === 'boolean';
    default:
      return typeof value === 'number' && isFinite(value);
  }
}

export function createNotificationDraft(tree: UiNode): NotificationDraft {
  const draft: NotificationDraft = {};
  for (const field of fieldsOf(tree)) draft[field.id] = seedOf(field);
  return draft;
}

export function applyNotificationDraftEvent(
  tree: UiNode,
  draft: NotificationDraft,
  event: UiNodeEvent,
): NotificationDraft {
  if (event.name !== UiComponentEvents.Change && event.name !== UiComponentEvents.Adjust) return draft;

  for (const field of fieldsOf(tree)) {
    if (field.id !== event.nodeId) continue;
    if (!acceptable(field.kind, event.data)) return draft;
    const next: NotificationDraft = {};
    for (const key of Object.keys(draft)) next[key] = draft[key];
    next[field.id] = event.data;
    return next;
  }

  return draft;
}

export function overlayNotificationDraft(tree: UiNode, draft: NotificationDraft): UiNode {
  const kind = kindOf(tree);
  let properties = tree.properties;

  if (kind !== null && declaresValueEvent(tree) && Object.prototype.hasOwnProperty.call(draft, tree.id)) {
    const value = draft[tree.id];
    const key = {
      text: UiComponentProperties.Text,
      toggle: UiComponentProperties.On,
      slider: UiComponentProperties.Level,
      segmented: UiComponentProperties.Selected,
    }[kind];

    if (value !== null && value !== undefined) {
      properties = {};
      const source = tree.properties ?? {};
      for (const name of Object.keys(source)) properties[name] = source[name];
      properties[key] = value;
    }
  }

  const children = tree.children?.map(child => overlayNotificationDraft(child, draft));
  const fallback = tree.fallback ? overlayNotificationDraft(tree.fallback, draft) : undefined;
  const sameChildren =
    children === undefined || children.every((child, index) => child === (tree.children as UiNode[])[index]);

  if (properties === tree.properties && sameChildren && fallback === tree.fallback) return tree;

  const next: UiNode = { id: tree.id, type: tree.type };
  if (tree.requiredComponentVersion !== undefined) next.requiredComponentVersion = tree.requiredComponentVersion;
  if (properties !== undefined) next.properties = properties;
  if (children !== undefined) next.children = children;
  if (fallback !== undefined) next.fallback = fallback;
  return next;
}

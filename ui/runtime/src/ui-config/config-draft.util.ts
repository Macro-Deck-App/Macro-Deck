import { UiConfigEvents } from './config-events';
import { UiConfigPrimitives } from './config-primitives';
import { UiNode, UiNodeEvent } from '../ui-framework/ui-node.interface';
import { emitsEvent } from '../ui-framework/node-properties.util';
import { UiConfigProperties } from './config-properties';

export function applyConfigDraftEvent(
  root: UiNode,
  data: Record<string, unknown>,
  event: UiNodeEvent,
): Record<string, unknown> {
  if (root.id === event.nodeId) return data;

  const field = locateField(scopedCandidates(root), event.nodeId);
  if (!field) return data;

  const key = localKey(field.id, null);
  const value = computeFieldValue(field, data[key], event);

  if (value === SKIP) return data;

  if (value === DROP) {
    const next = { ...data };
    delete next[key];
    return next;
  }

  return { ...data, [key]: value };
}

function locateField(nodes: readonly UiNode[], targetId: string): UiNode | undefined {
  for (const node of nodes) {
    if (node.id === targetId) return node;

    if (isNestingScope(node.type)) {
      if (subtreeContains(node, targetId)) return node;
      continue;
    }

    const found = locateField(scopedCandidates(node), targetId);
    if (found) return found;
  }

  return undefined;
}

function subtreeContains(node: UiNode, targetId: string): boolean {
  if (node.id === targetId) return true;

  for (const child of node.children ?? []) {
    if (subtreeContains(child, targetId)) return true;
  }

  return node.fallback ? subtreeContains(node.fallback, targetId) : false;
}

function scopedCandidates(node: UiNode): UiNode[] {
  const children = node.children ?? [];
  return node.fallback ? [...children, node.fallback] : children;
}

function isNestingScope(type: string): boolean {
  return type === UiConfigPrimitives.Object || type === UiConfigPrimitives.Array;
}

function isTransient(node: UiNode): boolean {
  return node.properties?.[UiConfigProperties.Transient] === true;
}

function computeFieldValue(field: UiNode, oldValue: unknown, event: UiNodeEvent): unknown {
  if (isTransient(field)) return SKIP;

  if (field.id === event.nodeId) {
    if (field.type === UiConfigPrimitives.Array
      && (event.name === UiConfigEvents.Add || event.name === UiConfigEvents.Remove)) {
      return reconcileArray(field, oldValue, event);
    }

    // A whole object or array can be bound at once (its own `value` is a composed JsonElement) -
    // that crosses exactly like a leaf's change, verbatim and unvalidated, per ADR 0050.
    if (event.name !== UiConfigEvents.Change) return oldValue;

    return isUnset(event.data, field.type) ? DROP : event.data;
  }

  // The composite binds its whole value, so an edit inside it is the provider's to make: it owns the
  // ids and the items the form is not showing, and the patch it sends next carries the result. Rebuilding
  // the composite here from the rendered children instead would drop everything they do not show - which
  // is how editing one state used to delete the others and strip their ids.
  if ((field.type === UiConfigPrimitives.Object || field.type === UiConfigPrimitives.Array)
    && UiConfigProperties.Value in (field.properties ?? {})) {
    return SKIP;
  }

  if (field.type === UiConfigPrimitives.Object) {
    const oldObject = isPlainRecord(oldValue) ? oldValue : {};
    const inner = locateField(scopedCandidates(field), event.nodeId);
    if (!inner) return oldObject;

    const innerKey = localKey(inner.id, field.id);
    const innerValue = computeFieldValue(inner, oldObject[innerKey], event);

    if (innerValue === SKIP) return oldObject;

    if (innerValue === DROP) {
      const next = { ...oldObject };
      delete next[innerKey];
      return next;
    }

    return { ...oldObject, [innerKey]: innerValue };
  }

  if (field.type === UiConfigPrimitives.Array) {
    return reconcileArray(field, oldValue, event);
  }

  // A leaf that is not the event's own target has nothing to change.
  return oldValue;
}

function reconcileArray(arrayNode: UiNode, oldValue: unknown, event: UiNodeEvent): unknown[] {
  const oldItems = Array.isArray(oldValue) ? oldValue : [];
  const byIdentity = new Map<string, unknown>();
  const unidentified: unknown[] = [];

  for (const item of oldItems) {
    const identity = itemIdentity(item);
    if (identity !== undefined && !byIdentity.has(identity)) {
      byIdentity.set(identity, item);
    } else if (identity === undefined) {
      unidentified.push(item);
    }
  }

  const removedKey = event.name === UiConfigEvents.Remove && event.nodeId === arrayNode.id
    && typeof event.data === 'string'
    ? localKey(event.data, arrayNode.id)
    : undefined;

  let unidentifiedCursor = 0;
  const result: unknown[] = [];

  for (const itemNode of scopedCandidates(arrayNode)) {
    const itemKey = localKey(itemNode.id, arrayNode.id);
    if (removedKey !== undefined && itemKey === removedKey) continue;

    const existing = (byIdentity.has(itemKey) ? byIdentity.get(itemKey) : unidentified[unidentifiedCursor++]) ?? {};
    const onPath = itemNode.id === event.nodeId || subtreeContains(itemNode, event.nodeId);
    result.push(onPath ? computeFieldValue(itemNode, existing, event) : existing);
  }

  return result;
}

function itemIdentity(value: unknown): string | undefined {
  if (typeof value === 'string') return value;
  if (isPlainRecord(value) && typeof value['id'] === 'string') return value['id'];
  return undefined;
}

function localKey(id: string, scope: string | null): string {
  if (scope === null) return id;
  const prefix = `${scope}.`;
  return id.startsWith(prefix) ? id.slice(prefix.length) : id;
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

// Provider-made writes (applying a preset, adopting a state provider, repairing a reference) reach
// the client as patches to input values and never as change events, so a draft folded only from
// events keeps the old values while the form shows the new ones - the user saves and the write is
// lost with nothing red anywhere. A node contributes exactly when it declares change, which is
// the honest test for an editable value and needs no separate list of input types kept in step.
export function composeConfigDraft(root: UiNode, data: Record<string, unknown>): Record<string, unknown> {
  const result = { ...data };

  for (const field of fieldsInScope(root)) {
    const key = localKey(field.id, null);
    const value = composeFieldFromTree(field, result[key]);
    if (value === DROP) delete result[key];
    else if (value !== SKIP) result[key] = value;
  }

  return result;
}

const SKIP = Symbol('skip');

const DROP = Symbol('drop');

const PICKER_TYPES: readonly string[] = [
  UiConfigPrimitives.Color,
  UiConfigPrimitives.Choice,
  UiConfigPrimitives.Icon,
  UiConfigPrimitives.WidgetTarget,
  UiConfigPrimitives.ActionPicker,
  UiConfigPrimitives.VariablePicker,
  UiConfigPrimitives.DevicePicker,
  UiConfigPrimitives.IntegrationPicker,
];

function isUnset(value: unknown, type?: string): boolean {
  if (value === null || value === undefined) return true;
  return value === '' && type !== undefined && PICKER_TYPES.includes(type);
}

function fieldsInScope(node: UiNode): UiNode[] {
  const fields: UiNode[] = [];

  for (const child of scopedCandidates(node)) {
    if (isTransient(child)) continue;

    if (isNestingScope(child.type) || emitsEvent(child, UiConfigEvents.Change)) {
      fields.push(child);
      continue;
    }

    fields.push(...fieldsInScope(child));
  }

  return fields;
}

function composeFieldFromTree(node: UiNode, oldValue: unknown): unknown | typeof SKIP {
  // A composite may bind its whole value at once instead of letting its children carry it - the
  // documented case on UiObjectInput and UiArrayInput. Where it does, that value is the provider's own
  // and is authoritative: rebuilding the composite from the child nodes instead would keep only what
  // those nodes happen to render, which for a state list means losing every state's id and every state
  // the form is not currently showing.
  const own = node.properties ?? {};
  if ((node.type === UiConfigPrimitives.Object || node.type === UiConfigPrimitives.Array)
    && UiConfigProperties.Value in own) {
    const value = own[UiConfigProperties.Value];
    return isUnset(value, node.type) ? DROP : value;
  }

  if (node.type === UiConfigPrimitives.Object) {
    const oldObject = isPlainRecord(oldValue) ? oldValue : {};
    const next = { ...oldObject };

    for (const child of fieldsInScope(node)) {
      const key = localKey(child.id, node.id);
      const value = composeFieldFromTree(child, oldObject[key]);
      if (value === DROP) delete next[key];
      else if (value !== SKIP) next[key] = value;
    }

    return next;
  }

  if (node.type === UiConfigPrimitives.Array) {
    const oldItems = Array.isArray(oldValue) ? oldValue : [];
    const byIdentity = new Map<string, unknown>();

    for (const item of oldItems) {
      const identity = itemIdentity(item);
      if (identity !== undefined && !byIdentity.has(identity)) byIdentity.set(identity, item);
    }

    return scopedCandidates(node).map(itemNode => {
      const existing = byIdentity.get(localKey(itemNode.id, node.id)) ?? {};
      const composed = composeFieldFromTree(itemNode, existing);
      return composed === SKIP ? existing : composed;
    });
  }

  const properties = node.properties ?? {};
  if (UiConfigProperties.Value in properties) {
    const value = properties[UiConfigProperties.Value];
    return isUnset(value, node.type) ? DROP : value;
  }

  return SKIP;
}

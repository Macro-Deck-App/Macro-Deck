import { UiNode } from './ui-node.interface';

export const UiPatchOperations = {
  SetProperties: 'set-properties',
  InsertNode: 'insert-node',
  RemoveNode: 'remove-node',
  ReplaceNode: 'replace-node',
  MoveNode: 'move-node',
} as const;

const KNOWN_OPERATIONS: ReadonlySet<string> = new Set(Object.values(UiPatchOperations));

export interface UiPatchOperation {
  op: string;
  nodeId: string;
  parentId?: string;
  index?: number;
  properties?: Record<string, unknown>;
  removedProperties?: string[];
  node?: UiNode;
}

export interface UiPatch {
  fromRevision: number;
  toRevision: number;
  operations: UiPatchOperation[];
}

// An ASCII alphanumeric character followed by up to 127 more ASCII alphanumeric characters, dots,
// hyphens or underscores - mirrors `MacroDeck.Ui.Model.Identity.UiIdentifier.IsValid`.
const NODE_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;

function isValidNodeId(value: string | undefined | null): value is string {
  return typeof value === 'string' && NODE_ID_PATTERN.test(value);
}

export function applyUiPatch(root: UiNode, currentRevision: number, patch: UiPatch): UiNode | null {
  if (
    currentRevision !== patch.fromRevision ||
    patch.toRevision <= patch.fromRevision ||
    !patch.operations ||
    patch.operations.length === 0
  ) {
    return null;
  }

  for (const operation of patch.operations) {
    if (!isOperationValid(operation)) {
      return null;
    }
  }

  let current = root;
  for (const operation of patch.operations) {
    const next = applyOperation(current, operation);
    if (next === null) {
      return null;
    }
    current = next;
  }

  return current;
}

function isOperationValid(operation: UiPatchOperation): boolean {
  if (!KNOWN_OPERATIONS.has(operation.op)) return false;
  if (!isValidNodeId(operation.nodeId)) return false;

  if (operation.properties && operation.removedProperties) {
    for (const key of operation.removedProperties) {
      if (Object.prototype.hasOwnProperty.call(operation.properties, key)) return false;
    }
  }

  switch (operation.op) {
    case UiPatchOperations.SetProperties:
      return !!operation.properties || !!operation.removedProperties;
    case UiPatchOperations.InsertNode:
      return !!operation.node && operation.node.id === operation.nodeId && operation.parentId !== undefined;
    case UiPatchOperations.ReplaceNode:
      return !!operation.node;
    case UiPatchOperations.MoveNode:
      return operation.parentId !== undefined;
    default:
      return true;
  }
}

function applyOperation(root: UiNode, operation: UiPatchOperation): UiNode | null {
  switch (operation.op) {
    case UiPatchOperations.SetProperties:
      return applySetProperties(root, operation);
    case UiPatchOperations.InsertNode:
      return applyInsertNode(root, operation);
    case UiPatchOperations.RemoveNode:
      return applyRemoveNode(root, operation);
    case UiPatchOperations.ReplaceNode:
      return applyReplaceNode(root, operation);
    case UiPatchOperations.MoveNode:
      return applyMoveNode(root, operation);
    default:
      return null;
  }
}

function applySetProperties(root: UiNode, operation: UiPatchOperation): UiNode | null {
  const target = findNode(root, operation.nodeId);
  if (!target) return null;

  const properties: Record<string, unknown> = { ...target.properties };

  if (operation.properties) {
    for (const [key, value] of Object.entries(operation.properties)) {
      properties[key] = value;
    }
  }

  if (operation.removedProperties) {
    for (const key of operation.removedProperties) {
      delete properties[key];
    }
  }

  return replaceNode(root, operation.nodeId, { ...target, properties });
}

function applyInsertNode(root: UiNode, operation: UiPatchOperation): UiNode | null {
  const parent = findNode(root, operation.parentId!);
  if (!parent) return null;

  const children = parent.children ?? [];
  const index = operation.index ?? children.length;
  if (index < 0 || index > children.length) return null;

  if (findDuplicateId(root, null, operation.node!)) return null;

  const nextChildren = [...children];
  nextChildren.splice(index, 0, operation.node!);
  return replaceNode(root, parent.id, { ...parent, children: nextChildren });
}

function applyRemoveNode(root: UiNode, operation: UiPatchOperation): UiNode | null {
  if (operation.nodeId === root.id) return null;
  return removeChild(root, operation.nodeId);
}

function applyReplaceNode(root: UiNode, operation: UiPatchOperation): UiNode | null {
  const replacement = operation.node!;

  if (operation.nodeId === root.id && replacement.id !== root.id) return null;
  if (!findNode(root, operation.nodeId)) return null;
  if (findDuplicateId(root, operation.nodeId, replacement)) return null;

  return replaceNode(root, operation.nodeId, replacement);
}

function applyMoveNode(root: UiNode, operation: UiPatchOperation): UiNode | null {
  if (operation.nodeId === root.id) return null;

  const node = findNode(root, operation.nodeId);
  if (!node) return null;

  const removedRoot = removeChild(root, operation.nodeId);
  if (!removedRoot) return null;

  const parent = findNode(removedRoot, operation.parentId!);
  if (!parent) return null;

  const children = parent.children ?? [];
  const index = operation.index ?? children.length;
  if (index < 0 || index > children.length) return null;

  const nextChildren = [...children];
  nextChildren.splice(index, 0, node);
  return replaceNode(removedRoot, parent.id, { ...parent, children: nextChildren });
}

function findNode(node: UiNode, id: string): UiNode | null {
  if (node.id === id) return node;

  for (const child of node.children ?? []) {
    const found = findNode(child, id);
    if (found) return found;
  }

  return node.fallback ? findNode(node.fallback, id) : null;
}

function replaceNode(node: UiNode, targetId: string, replacement: UiNode): UiNode | null {
  if (node.id === targetId) return replacement;

  const children = node.children ?? [];
  for (let i = 0; i < children.length; i++) {
    const updatedChild = replaceNode(children[i], targetId, replacement);
    if (updatedChild) {
      const nextChildren = [...children];
      nextChildren[i] = updatedChild;
      return { ...node, children: nextChildren };
    }
  }

  if (node.fallback) {
    const updatedFallback = replaceNode(node.fallback, targetId, replacement);
    if (updatedFallback) return { ...node, fallback: updatedFallback };
  }

  return null;
}

function removeChild(node: UiNode, targetId: string): UiNode | null {
  const children = node.children ?? [];
  const index = children.findIndex(child => child.id === targetId);

  if (index >= 0) {
    const nextChildren = [...children];
    nextChildren.splice(index, 1);
    return { ...node, children: nextChildren };
  }

  for (let i = 0; i < children.length; i++) {
    const updatedChild = removeChild(children[i], targetId);
    if (updatedChild) {
      const nextChildren = [...children];
      nextChildren[i] = updatedChild;
      return { ...node, children: nextChildren };
    }
  }

  if (node.fallback) {
    const updatedFallback = removeChild(node.fallback, targetId);
    if (updatedFallback) return { ...node, fallback: updatedFallback };
  }

  return null;
}

function findDuplicateId(root: UiNode, excludedId: string | null, newSubtree: UiNode): string | null {
  const existingIds = new Set<string>();
  collectIds(root, excludedId, existingIds);

  for (const id of subtreeIds(newSubtree)) {
    if (existingIds.has(id)) return id;
  }

  return null;
}

function collectIds(node: UiNode, excludedId: string | null, ids: Set<string>): void {
  if (excludedId !== null && node.id === excludedId) return;

  ids.add(node.id);

  for (const child of node.children ?? []) {
    collectIds(child, excludedId, ids);
  }

  if (node.fallback) {
    collectIds(node.fallback, excludedId, ids);
  }
}

function* subtreeIds(node: UiNode): IterableIterator<string> {
  yield node.id;

  for (const child of node.children ?? []) {
    yield* subtreeIds(child);
  }

  if (node.fallback) {
    yield* subtreeIds(node.fallback);
  }
}

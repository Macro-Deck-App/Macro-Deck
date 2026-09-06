import {
  ActionBlock,
  ActionBlockDefinition,
  ActionBlockParameter,
  ActionFlow,
  ParameterValue,
  isSecretReference,
} from './action-builder.interface';
import { createEmptyComparison } from './condition-expression.util';

export function generateBlockId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
  return `block-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

export function descendantLists(block: ActionBlock): ActionBlock[][] {
  const lists: ActionBlock[][] = [];
  if (block.children) lists.push(block.children);
  if (block.branches) for (const br of block.branches) lists.push(br.children);
  return lists;
}

export function mapDescendants(
  block: ActionBlock,
  transform: (list: ActionBlock[]) => ActionBlock[],
): ActionBlock {
  return {
    ...block,
    children: block.children ? transform(block.children) : block.children,
    branches: block.branches
      ? block.branches.map(b => ({ ...b, children: transform(b.children) }))
      : block.branches,
  };
}

export function resolveList(flows: ActionFlow[], listId: string): ActionBlock[] | null {
  if (listId.startsWith('flow:')) {
    const triggerId = listId.slice('flow:'.length);
    const flow = flows.find(f => f.triggerId === triggerId);
    return flow?.children ?? null;
  }
  if (listId.startsWith('children:')) {
    const blockId = listId.slice('children:'.length);
    const parent = findBlock(flows, blockId);
    if (!parent) return null;
    parent.children ??= [];
    return parent.children;
  }
  if (listId.startsWith('branch:')) {
    const rest = listId.slice('branch:'.length);
    const sep = rest.indexOf(':');
    if (sep === -1) return null;
    const blockId = rest.slice(0, sep);
    const branchId = rest.slice(sep + 1);
    const parent = findBlock(flows, blockId);
    const branch = parent?.branches?.find(b => b.id === branchId);
    if (!branch) return null;
    branch.children ??= [];
    return branch.children;
  }
  return null;
}

export function removeBlockAt(flows: ActionFlow[], listId: string, index: number): ActionBlock | null {
  const list = resolveList(flows, listId);
  if (!list || index < 0 || index >= list.length) return null;
  return list.splice(index, 1)[0] ?? null;
}

export function insertBlockAt(flows: ActionFlow[], listId: string, index: number, block: ActionBlock): void {
  const list = resolveList(flows, listId);
  if (!list) return;
  list.splice(Math.max(0, Math.min(index, list.length)), 0, block);
}

export function findBlock(flows: ActionFlow[], blockId: string): ActionBlock | null {
  for (const flow of flows) {
    for (const block of flow.children) {
      const found = findBlockRecursive(block, blockId);
      if (found) return found;
    }
  }
  return null;
}

function findBlockRecursive(block: ActionBlock, blockId: string): ActionBlock | null {
  if (block.id === blockId) return block;
  for (const list of descendantLists(block)) {
    for (const c of list) {
      const found = findBlockRecursive(c, blockId);
      if (found) return found;
    }
  }
  return null;
}

export function containsBlock(block: ActionBlock, blockId: string): boolean {
  if (block.id === blockId) return true;
  for (const list of descendantLists(block)) {
    if (list.some(child => containsBlock(child, blockId))) return true;
  }
  return false;
}

export function isInvalidNestedMove(block: ActionBlock, targetListId: string): boolean {
  if (targetListId.startsWith('children:')) {
    return containsBlock(block, targetListId.slice('children:'.length));
  }
  if (targetListId.startsWith('branch:')) {
    const rest = targetListId.slice('branch:'.length);
    const sep = rest.indexOf(':');
    const parentId = sep === -1 ? rest : rest.slice(0, sep);
    return containsBlock(block, parentId);
  }
  return false;
}

export function collectListIds(block: ActionBlock, ids: string[]): void {
  if (block.children !== undefined) {
    ids.push(`children:${block.id}`);
    block.children.forEach(child => collectListIds(child, ids));
  }
  if (block.branches !== undefined) {
    block.branches.forEach(branch => {
      ids.push(`branch:${block.id}:${branch.id}`);
      branch.children.forEach(child => collectListIds(child, ids));
    });
  }
}

export function isListInsideLoop(flows: ActionFlow[], listId: string): boolean {
  if (!listId.startsWith('children:') && !listId.startsWith('branch:')) return false;

  const matchesList = (block: ActionBlock): boolean =>
    listId === `children:${block.id}` ||
    (block.branches?.some(b => listId === `branch:${block.id}:${b.id}`) ?? false);

  const search = (
    list: ActionBlock[],
    ancestors: ActionBlock[],
  ): { ancestors: ActionBlock[] } | null => {
    for (const block of list) {
      if (matchesList(block)) return { ancestors: [...ancestors, block] };
      const nextAncestors = [...ancestors, block];
      for (const sub of descendantLists(block)) {
        const found = search(sub, nextAncestors);
        if (found) return found;
      }
    }
    return null;
  };

  for (const flow of flows) {
    const found = search(flow.children, []);
    if (!found) continue;
    return found.ancestors.some(a => a.type === 'loop');
  }
  return false;
}

export function normalizeLegacyParamTypes(flows: ActionFlow[]): ActionFlow[] {
  const normalizeBlock = (block: ActionBlock): ActionBlock => ({
    ...block,
    parameters: block.parameters?.map(p =>
      (p.type as string) === 'select' ? { ...p, type: 'choice' as const } : p,
    ),
    children: block.children?.map(normalizeBlock),
    branches: block.branches?.map(branch => ({
      ...branch,
      children: branch.children.map(normalizeBlock),
    })),
  });

  return flows.map(flow => ({ ...flow, children: flow.children.map(normalizeBlock) }));
}

// Descriptors always come from the definition, never from what was stored: a block migrated from
// Macro Deck 2 stores only name + value, and label has to resolve in the viewer's language.
// Only value and the cached valueLabel come from the stored parameter - the label is a
// per-selection cache the definition cannot re-derive. This also keeps a parameter added to a
// definition after a block was saved from staying invisible on that block forever.
export function hydrateBlockParameters(
  flows: ActionFlow[],
  lookupDefinition: (integrationId: string, actionId: string) => ActionBlockDefinition | undefined,
): ActionFlow[] {
  const hydrateBlock = (block: ActionBlock): ActionBlock => {
    const def = block.integrationId && block.actionId
      ? lookupDefinition(block.integrationId, block.actionId)
      : undefined;

    let parameters = block.parameters;
    if (def) {
      const stored = block.parameters ?? [];
      const declaredNames = new Set((def.parameters ?? []).map(p => p.name));
      const declared = (def.parameters ?? []).map(defParam => {
        const storedParam = stored.find(p => p.name === defParam.name);
        if (!storedParam) return { ...defParam, value: defaultParameterValue(defParam) };
        return storedParam.valueLabel === undefined
          ? { ...defParam, value: storedParam.value }
          : { ...defParam, value: storedParam.value, valueLabel: storedParam.valueLabel };
      });
      // A stored parameter the definition no longer declares is kept, not discarded - it may be a
      // placeholder action's captured configuration, or one an integration dropped from its
      // definition since the block was saved.
      parameters = [...declared, ...stored.filter(p => !declaredNames.has(p.name))];
    }
    // else: a missing/disabled integration, or the migration placeholder before it loads - keep the
    // block's stored parameters untouched rather than blanking it out.

    return {
      ...block,
      parameters,
      children: block.children?.map(hydrateBlock),
      branches: block.branches?.map(branch => ({ ...branch, children: branch.children.map(hydrateBlock) })),
    };
  };

  return flows.map(flow => ({ ...flow, children: flow.children.map(hydrateBlock) }));
}

export function cloneBlockWithNewIds(block: ActionBlock): ActionBlock {
  const clone = structuredClone(block);
  const reId = (b: ActionBlock): void => {
    b.id = generateBlockId();
    b.children?.forEach(reId);
    b.branches?.forEach(branch => {
      branch.id = generateBlockId();
      branch.children.forEach(reId);
    });
  };
  reId(clone);
  return clone;
}

export function removeBlockFromFlows(
  flows: ActionFlow[],
  blockId: string,
): { flows: ActionFlow[]; removed: boolean } {
  let removed = false;

  const removeFromList = (list: ActionBlock[]): ActionBlock[] =>
    list
      .filter(block => {
        if (block.id !== blockId) return true;
        removed = true;
        return false;
      })
      .map(block => mapDescendants(block, removeFromList));

  const next = flows.map(flow => ({ ...flow, children: removeFromList(flow.children) }));
  return { flows: next, removed };
}

export function insertAfterBlock(flows: ActionFlow[], afterBlockId: string, block: ActionBlock): boolean {
  const insertInto = (list: ActionBlock[]): boolean => {
    const index = list.findIndex(b => b.id === afterBlockId);
    if (index !== -1) {
      list.splice(index + 1, 0, block);
      return true;
    }
    return list.some(b => descendantLists(b).some(insertInto));
  };
  return flows.some(flow => insertInto(flow.children));
}

export function collectSecretIds(value: unknown, ids: string[] = []): string[] {
  if (isSecretReference(value)) {
    ids.push(value.$secret);
  } else if (Array.isArray(value)) {
    value.forEach(item => collectSecretIds(item, ids));
  } else if (typeof value === 'object' && value !== null) {
    Object.values(value).forEach(item => collectSecretIds(item, ids));
  }
  return ids;
}

export function mapSecretReferences<T>(value: T, map: ReadonlyMap<string, string | null>): T {
  if (isSecretReference(value)) {
    const mapped = map.get(value.$secret);
    return (mapped ? { $secret: mapped } : null) as T;
  }
  if (Array.isArray(value)) {
    return value.map(item => mapSecretReferences(item, map)) as T;
  }
  if (typeof value === 'object' && value !== null) {
    return Object.fromEntries(
      Object.entries(value).map(([key, item]) => [key, mapSecretReferences(item, map)]),
    ) as T;
  }
  return value;
}

export function defaultParameterValue(p: Omit<ActionBlockParameter, 'value'>): ParameterValue {
  if (p.defaultValue !== undefined && p.defaultValue !== null) {
    return p.defaultValue;
  }

  switch (p.type) {
    case 'number':
    case 'duration':
      return p.min ?? 0;
    case 'boolean':
      return false;
    case 'choice':
      return (p.options?.[0]?.value as ParameterValue) ?? '';
    case 'multiselect':
      return [];
    case 'hotkey':
    case 'password':
    case 'secret':
      return null;
    case 'keyvalue':
    case 'object':
      return {};
    case 'array':
      return [];
    case 'color':
      return '#3b82f6';
    case 'keyboard-sequence':
      return { steps: [], repeat: 1, repeatDelayMs: 0 };
    case 'keyboard-combo':
      return { modifiers: [], key: '' };
    default:
      return '';
  }
}

export function createBlockFromDefinition(def: ActionBlockDefinition): ActionBlock {
  const block: ActionBlock = {
    id: generateBlockId(),
    type: def.type,
    blockType: def.blockType,
    label: def.label,
    color: def.color,
    integrationId: def.integrationId,
    actionId: def.actionId,
    parameters: def.parameters?.map(p => ({
      ...p,
      value: defaultParameterValue(p),
    })),
  };
  if (def.hasChildren) block.children = [];
  if (def.hasCondition) {
    block.condition = createEmptyComparison();
  }
  if (def.hasBranches) {
    block.branches = [
      {
        id: generateBlockId(),
        kind: 'if',
        condition: createEmptyComparison(),
        children: [],
      },
      { id: generateBlockId(), kind: 'else', children: [] },
    ];
  }
  return block;
}

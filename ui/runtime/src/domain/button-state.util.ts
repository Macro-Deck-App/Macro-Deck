import {
  ActionButtonData,
  ButtonState,
  ButtonStateDefinition,
  ButtonStateMapping,
} from './widget.interface';

export const DEFAULT_OFF_STATE_ID = 'off';
export const DEFAULT_ON_STATE_ID = 'on';

export function defaultStateDefinitions(seed?: ActionButtonData): ButtonStateDefinition[] {
  // `off` deliberately carries no background: seeding it from the single-state colour, while that
  // colour also stayed on as the root fallback, is what made a background the user had moved away
  // from reappear on the next save. It falls through to the reader's own accent instead. `on` keeps a
  // colour of its own - a fresh default rather than anything carried over - so a button that has just
  // gained states still reads as two visibly different ones.
  return [
    { id: DEFAULT_OFF_STATE_ID, label: 'Off', appearance: { label: seed?.label } },
    { id: DEFAULT_ON_STATE_ID, label: 'On', appearance: { backgroundColor: '#ef4444', label: seed?.label } },
  ];
}

export function createStateId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  return `state-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

export function isEffectiveMapping(mapping: ButtonStateMapping | undefined): mapping is ButtonStateMapping {
  // A rule entry can be anything once it round-trips through untyped storage (null, a string, ...),
  // and `stateId` itself can be a non-string truthy value (e.g. a number) - both must be rejected the
  // same way the host's ReadMapping rejects them, so the two sides never disagree about whether a
  // stored mapping is effective.
  return !!mapping && Array.isArray(mapping.rules) && mapping.rules.some(rule =>
    !!rule && typeof rule === 'object' && typeof rule.stateId === 'string' && rule.stateId !== '');
}

export function findState(data: ActionButtonData, stateId: string): ButtonStateDefinition | undefined {
  return data.states?.find(state => state.id === stateId);
}

export function findStateIndex(data: ActionButtonData, stateId: string): number {
  return data.states?.findIndex(state => state.id === stateId) ?? -1;
}

export function resolveActiveStateId(data: ActionButtonData, pushedStateId?: string): string | undefined {
  if (pushedStateId !== undefined && findState(data, pushedStateId)) {
    return pushedStateId;
  }
  if (data.activeStateId !== undefined && findState(data, data.activeStateId)) {
    return data.activeStateId;
  }
  return data.states?.[0]?.id;
}

export interface ProvidedStateAppearance {
  label?: string;
  backgroundColor?: string;
  labelColor?: string;
  iconId?: string;
}

export interface ProvidedState {
  id: string;
  label: string;
  defaultAppearance?: ProvidedStateAppearance;
}

export function adoptProvidedStates(
  current: ButtonStateDefinition[],
  provided: ProvidedState[],
): ButtonStateDefinition[] {
  const existing = new Map(current.map(state => [state.id, state]));
  return provided.map(state => ({
    id: state.id,
    label: state.label,
    appearance: existing.has(state.id) ? existing.get(state.id)!.appearance : seedAppearance(state),
  }));
}

function seedAppearance(state: ProvidedState): ButtonState {
  const defaults = state.defaultAppearance;
  const appearance: ButtonState = { label: defaults?.label ?? state.label };
  if (defaults?.backgroundColor) appearance.backgroundColor = defaults.backgroundColor;
  if (defaults?.labelColor) appearance.labelColor = defaults.labelColor;
  if (defaults?.iconId) appearance.iconId = defaults.iconId;
  return appearance;
}

const STATE_ID_PATTERN = /^[a-z0-9][a-z0-9_-]{0,63}$/;

export function isValidStateId(id: unknown): id is string {
  return typeof id === 'string' && STATE_ID_PATTERN.test(id);
}

export function normalizeStates(states: unknown): ButtonStateDefinition[] {
  if (!Array.isArray(states)) {
    return [];
  }
  const seenIds = new Set<string>();
  const normalized: ButtonStateDefinition[] = [];
  for (const entry of states) {
    if (typeof entry !== 'object' || entry === null) {
      continue;
    }
    const candidate = entry as Partial<ButtonStateDefinition>;
    let id = isValidStateId(candidate.id) ? candidate.id : createStateId();
    while (seenIds.has(id)) {
      id = createStateId();
    }
    seenIds.add(id);
    normalized.push({ ...candidate, id } as ButtonStateDefinition);
  }
  return normalized;
}

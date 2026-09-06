import { Injectable, computed, effect, inject, signal } from '@angular/core';
import {
  AppStrings,
  createPendingStateLabelVariable,
  createPendingStateVariable,
  type CreateVariableRequest,
  DEFAULT_OFF_STATE_ID,
  STATE_LABEL_VARIABLE_NAME,
  STATE_VARIABLE_NAME,
  type UpdateVariableRequest,
  type Variable,
  type VariablesChangedEvent,
  type VariableScope,
  type VariableType,
} from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';

@Injectable({ providedIn: 'root' })
export class VariableService {
  private readonly ipc = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly variables = signal<Variable[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private inFlightLoad: Promise<void> | null = null;

  private loadOverlay: Map<string, Variable | null> | null = null;

  readonly globalVariables = computed(() =>
    this.variables()
      .filter(v => v.scope === 'global')
      .sort(byClassificationThenName),
  );

  constructor() {
    this.subscribeToEvents();

    effect(() => {
      if (this.ipc.connectionStateSignal() === 'connected') {
        void this.loadVariables();
      }
    });
  }

  visibleForContext(scope: VariableScope, scopeRefId?: string): Variable[] {
    const all = this.variables();
    if (scope === 'global' || !scopeRefId) {
      return all.filter(v => v.scope === 'global').sort(byClassificationThenName);
    }
    const locals = all.filter(v => v.scope === scope && v.scopeRefId === scopeRefId);
    const localNames = new Set(locals.map(v => v.name));
    const globals = all.filter(v => v.scope === 'global' && !localNames.has(v.name));
    return [...locals, ...globals].sort(byClassificationThenName);
  }

  private pendingStateVariables(scopeRefId: string, stateMode: boolean): Variable[] {
    if (!stateMode) return [];
    const existing = this.variables().filter(v => v.scope === 'widget' && v.scopeRefId === scopeRefId);
    const pending: Variable[] = [];
    if (!existing.some(v => v.name === STATE_VARIABLE_NAME)) {
      pending.push(createPendingStateVariable(scopeRefId, DEFAULT_OFF_STATE_ID));
    }
    if (!existing.some(v => v.name === STATE_LABEL_VARIABLE_NAME)) {
      pending.push(createPendingStateLabelVariable(scopeRefId, 'Off'));
    }
    return pending;
  }

  visibleForActionButtonEditor(scopeRefId: string | undefined, stateMode: boolean): Variable[] {
    if (!scopeRefId) {
      return this.visibleForContext('global');
    }
    const all = this.variables();
    let locals = all.filter(v => v.scope === 'widget' && v.scopeRefId === scopeRefId);
    if (stateMode) {
      locals = [...locals, ...this.pendingStateVariables(scopeRefId, stateMode)];
    } else {
      locals = locals.filter(v =>
        !((v.name === STATE_VARIABLE_NAME || v.name === STATE_LABEL_VARIABLE_NAME) && v.classification === 'widget'));
    }
    const localNames = new Set(locals.map(v => v.name));
    const globals = all.filter(v => v.scope === 'global' && !localNames.has(v.name));
    return [...locals, ...globals].sort(byClassificationThenName);
  }

  loadVariables(): Promise<void> {
    this.inFlightLoad ??= this.runLoad().finally(() => {
      this.inFlightLoad = null;
    });
    return this.inFlightLoad;
  }

  private async runLoad(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    const overlay = new Map<string, Variable | null>();
    this.loadOverlay = overlay;
    try {
      const response = await this.ipc.getVariables();
      const snapshot = response.variables ?? [];
      this.variables.set(overlay.size === 0 ? snapshot : applyOverlay(snapshot, overlay));
    } catch (error) {
      console.error('Failed to load variables:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Variable.LoadFailed));
    } finally {
      this.loadOverlay = null;
      this.isLoading.set(false);
    }
  }

  async create(request: CreateVariableRequest): Promise<Variable | null> {
    const response = await this.ipc.createVariable(request);
    if (!response.success || !response.variable) {
      console.warn('createVariable failed:', response.error);
      return null;
    }
    this.upsertLocal(response.variable);
    return response.variable;
  }

  async update(request: UpdateVariableRequest): Promise<Variable | null> {
    const response = await this.ipc.updateVariable(request);
    if (!response.success || !response.variable) {
      console.warn('updateVariable failed:', response.error);
      return null;
    }
    this.upsertLocal(response.variable);
    return response.variable;
  }

  async delete(id: string): Promise<boolean> {
    const response = await this.ipc.deleteVariable({ id });
    if (!response.success) {
      console.warn('deleteVariable failed:', response.error);
      return false;
    }
    this.deleteLocal(id);
    return true;
  }

  async setValue(id: string, value: string | undefined,
                 resourceIntegrationId?: string, resourceKey?: string): Promise<Variable | null> {
    const response = await this.ipc.setVariableValue({ id, value, resourceIntegrationId, resourceKey });
    if (!response.success || !response.variable) {
      console.warn('setVariableValue failed:', response.error);
      return null;
    }
    // A write handed to the variable's owner comes back with the value as it still stands - the
    // authoritative one arrives later on the read side. Echoing it locally would snap the field
    // back to the old value the instant the user committed a new one.
    if (!response.pending) {
      this.upsertLocal(response.variable);
    }
    return response.variable;
  }

  async sanitizeName(input: string): Promise<{ sanitized: string; isValid: boolean }> {
    if (!input) return { sanitized: '', isValid: false };
    try {
      const response = await this.ipc.sanitizeVariableName({ input });
      return { sanitized: response.sanitized, isValid: response.isValid };
    } catch {
      return localSanitize(input);
    }
  }

  sanitizeNameLocal(input: string): { sanitized: string; isValid: boolean } {
    return localSanitize(input);
  }

  resolve(name: string, scope: VariableScope = 'global', scopeRefId?: string): Variable | undefined {
    const list = this.variables();
    if (scope !== 'global' && scopeRefId) {
      const local = list.find(v => v.scope === scope && v.scopeRefId === scopeRefId && v.name === name);
      if (local) return local;
    }
    return list.find(v => v.scope === 'global' && v.name === name);
  }

  filterByTypes(scope: VariableScope, scopeRefId: string | undefined,
                acceptedTypes: VariableType[] | undefined): Variable[] {
    const visible = this.visibleForContext(scope, scopeRefId);
    if (!acceptedTypes || acceptedTypes.length === 0) return visible;
    return visible.filter(v => acceptedTypes.includes(v.type));
  }

  private subscribeToEvents(): void {
    this.ipc.onNotification<VariablesChangedEvent>('VariablesChangedEvent').subscribe(evt => {
      this.applyBatch(evt.upserted ?? [], evt.deletedIds ?? []);
    });
  }

  applyBatch(upserted: Variable[], deletedIds: string[]): void {
    if (upserted.length === 0 && deletedIds.length === 0) {
      return;
    }

    const deleted = new Set(deletedIds);
    for (const variable of upserted) {
      if (!deleted.has(variable.id)) {
        this.loadOverlay?.set(variable.id, variable);
      }
    }
    for (const id of deleted) {
      this.loadOverlay?.set(id, null);
    }

    this.variables.update(list => {
      const indexById = new Map<string, number>();
      list.forEach((v, i) => indexById.set(v.id, i));

      const next = list.slice();
      for (const variable of upserted) {
        if (deleted.has(variable.id)) continue;
        const at = indexById.get(variable.id);
        if (at === undefined) {
          indexById.set(variable.id, next.length);
          next.push(variable);
        } else {
          next[at] = variable;
        }
      }

      return deleted.size === 0 ? next : next.filter(v => !deleted.has(v.id));
    });
  }

  private upsertLocal(variable: Variable): void {
    this.loadOverlay?.set(variable.id, variable);
    this.variables.update(list => {
      const idx = list.findIndex(v => v.id === variable.id);
      if (idx === -1) return [...list, variable];
      const next = list.slice();
      next[idx] = variable;
      return next;
    });
  }

  private deleteLocal(id: string): void {
    this.loadOverlay?.set(id, null);
    this.variables.update(list => list.filter(v => v.id !== id));
  }
}

function applyOverlay(snapshot: Variable[], overlay: Map<string, Variable | null>): Variable[] {
  const merged = snapshot.filter(v => overlay.get(v.id) !== null);
  for (const [id, variable] of overlay) {
    if (variable === null) continue;
    const idx = merged.findIndex(v => v.id === id);
    if (idx === -1) merged.push(variable);
    else merged[idx] = variable;
  }
  return merged;
}

const CLASSIFICATION_ORDER: Record<string, number> = {
  user: 0,
  integration: 1,
  widget: 2,
};

function byClassificationThenName(a: Variable, b: Variable): number {
  const c = (CLASSIFICATION_ORDER[a.classification] ?? 99) -
            (CLASSIFICATION_ORDER[b.classification] ?? 99);
  if (c !== 0) return c;
  return a.name.localeCompare(b.name);
}

const VALID_NAME = /^[a-z][a-z0-9_]*$/;
const PUBLIC_PREFIX = 'vars.';

function localSanitize(input: string): { sanitized: string; isValid: boolean } {
  if (!input) return { sanitized: '', isValid: false };
  let trimmed = input.trim();
  if (trimmed.toLowerCase().startsWith(PUBLIC_PREFIX)) {
    trimmed = trimmed.slice(PUBLIC_PREFIX.length);
  }
  const folded = trimmed.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
  let out = '';
  for (const ch of folded) {
    if (/[a-z0-9_]/.test(ch)) out += ch;
    else if (/[A-Z]/.test(ch)) out += ch.toLowerCase();
    else out += '_';
  }
  out = out.replace(/_+/g, '_').replace(/^_+|_+$/g, '');
  if (out.length > 0 && /[0-9]/.test(out[0])) {
    out = 'v_' + out;
  }
  return { sanitized: out, isValid: VALID_NAME.test(out) };
}


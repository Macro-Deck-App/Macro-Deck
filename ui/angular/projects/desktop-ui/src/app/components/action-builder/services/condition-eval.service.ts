import { Injectable, OnDestroy, signal } from '@angular/core';

import type { ConditionExpression, VariableScope } from '@macro-deck/runtime';
import type { TemplatePreviewService } from '../../../domain/template-preview.interface';
import type { LeafBadgeState } from '../../condition-builder/condition-builder.component';

export type ConditionEvalState =
  | { kind: 'pending' }
  | { kind: 'ok'; result: boolean }
  | { kind: 'error'; message: string };

interface Slot {
  expression: ConditionExpression;
  serialized: string;
  eventId?: string;
}

@Injectable()
export class ConditionEvalService implements OnDestroy {
  private static readonly POLL_INTERVAL_MS = 2000;
  private static readonly EDIT_DEBOUNCE_MS = 300;

  readonly evals = signal<Record<string, ConditionEvalState>>({});
  readonly leaves = signal<Record<string, Record<string, LeafBadgeState>>>({});

  private preview?: TemplatePreviewService;
  private scope: VariableScope = 'global';
  private scopeRefId?: string;

  private readonly slots = new Map<string, Slot>();
  private readonly requestSeq = new Map<string, number>();
  private readonly debounceHandles = new Map<string, ReturnType<typeof setTimeout>>();
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  configure(preview: TemplatePreviewService | undefined, scope: VariableScope, scopeRefId?: string): void {
    const scopeChanged = this.preview && (scope !== this.scope || scopeRefId !== this.scopeRefId);
    this.preview = preview;
    this.scope = scope;
    this.scopeRefId = scopeRefId;
    if (!preview) return;
    this.ensurePolling();
    if (scopeChanged) this.evaluateAll();
  }

  register(key: string, expression: ConditionExpression, eventId?: string): void {
    const serialized = JSON.stringify([expression, eventId ?? null]);
    const existing = this.slots.get(key);
    if (existing?.serialized === serialized) return;

    const isNew = !existing;
    this.slots.set(key, { expression, serialized, eventId });

    if (!this.preview) return;
    if (isNew) {
      void this.evaluate(serialized);
    } else {
      const pending = this.debounceHandles.get(key);
      if (pending) clearTimeout(pending);
      this.debounceHandles.set(key, setTimeout(() => {
        this.debounceHandles.delete(key);
        const slot = this.slots.get(key);
        if (slot) void this.evaluate(slot.serialized);
      }, ConditionEvalService.EDIT_DEBOUNCE_MS));
    }
  }

  unregister(key: string): void {
    this.slots.delete(key);
    const pending = this.debounceHandles.get(key);
    if (pending) {
      clearTimeout(pending);
      this.debounceHandles.delete(key);
    }
    if (key in this.evals()) {
      this.evals.update(prev => {
        const { [key]: _, ...rest } = prev;
        return rest;
      });
    }
    if (key in this.leaves()) {
      this.leaves.update(prev => {
        const { [key]: _, ...rest } = prev;
        return rest;
      });
    }
  }

  state(key: string): ConditionEvalState | undefined {
    return this.evals()[key];
  }

  leafState(key: string, leafId: string): LeafBadgeState | undefined {
    return this.leaves()[key]?.[leafId];
  }

  ngOnDestroy(): void {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
    for (const handle of this.debounceHandles.values()) clearTimeout(handle);
    this.debounceHandles.clear();
    this.slots.clear();
  }

  private ensurePolling(): void {
    if (this.pollHandle !== null) return;
    this.pollHandle = setInterval(() => {
      if (document.hidden) return;
      this.evaluateAll();
    }, ConditionEvalService.POLL_INTERVAL_MS);
  }

  private evaluateAll(): void {
    const unique = new Set<string>();
    for (const slot of this.slots.values()) unique.add(slot.serialized);
    for (const serialized of unique) void this.evaluate(serialized);
  }

  private async evaluate(serialized: string): Promise<void> {
    if (!this.preview) return;
    const keys = this.keysFor(serialized);
    if (keys.length === 0) return;

    const seq = (this.requestSeq.get(serialized) ?? 0) + 1;
    this.requestSeq.set(serialized, seq);

    const current = this.evals();
    const fresh = keys.filter(k => !current[k]);
    if (fresh.length > 0) {
      this.patchEvals(fresh, { kind: 'pending' });
    }

    const [expression, eventId] = JSON.parse(serialized) as [ConditionExpression, string | null];
    try {
      const r = await this.preview.evaluateExpression(
        expression,
        this.scope,
        this.scopeRefId,
        eventId ?? undefined,
      );
      if (this.requestSeq.get(serialized) !== seq) return;
      const liveKeys = this.keysFor(serialized);
      this.patchEvals(liveKeys, { kind: 'ok', result: r.result });
      this.patchLeaves(liveKeys, r.leaves);
    } catch (err) {
      if (this.requestSeq.get(serialized) !== seq) return;
      const message = err instanceof Error ? err.message : String(err);
      this.patchEvals(this.keysFor(serialized), { kind: 'error', message });
    }
  }

  private keysFor(serialized: string): string[] {
    const keys: string[] = [];
    for (const [key, slot] of this.slots) {
      if (slot.serialized === serialized) keys.push(key);
    }
    return keys;
  }

  private patchEvals(keys: string[], value: ConditionEvalState): void {
    if (keys.length === 0) return;
    this.evals.update(prev => {
      const next = { ...prev };
      for (const key of keys) next[key] = value;
      return next;
    });
  }

  private patchLeaves(
    keys: string[],
    leaves: Record<string, { result: boolean | null; leftDisplay: string; rightDisplay: string; error?: string }>,
  ): void {
    if (keys.length === 0) return;
    const mapped: Record<string, LeafBadgeState> = {};
    for (const [leafId, leaf] of Object.entries(leaves)) {
      if (leaf.error) {
        mapped[leafId] = { kind: 'error', message: leaf.error };
      } else if (leaf.result === null) {
        mapped[leafId] = { kind: 'short-circuit' };
      } else {
        mapped[leafId] = {
          kind: 'ok',
          result: leaf.result,
          leftDisplay: leaf.leftDisplay,
          rightDisplay: leaf.rightDisplay,
        };
      }
    }
    this.leaves.update(prev => {
      const next = { ...prev };
      for (const key of keys) next[key] = mapped;
      return next;
    });
  }
}

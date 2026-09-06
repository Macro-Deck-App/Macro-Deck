import { Injectable, computed, signal } from '@angular/core';
import { ActionBlock } from '@macro-deck/runtime';

export type ActionFlowOwner =
  | { kind: 'widget'; widgetId: string }
  | { kind: 'script'; scriptId: string }
  | { kind: 'automation'; automationId: string };

export function sameActionFlowOwner(a: ActionFlowOwner | null, b: ActionFlowOwner | null): boolean {
  if (!a || !b || a.kind !== b.kind) return false;
  if (a.kind === 'widget' && b.kind === 'widget') return a.widgetId === b.widgetId;
  if (a.kind === 'script' && b.kind === 'script') return a.scriptId === b.scriptId;
  if (a.kind === 'automation' && b.kind === 'automation') return a.automationId === b.automationId;
  return false;
}

export interface ActionClipboardEntry {
  block: ActionBlock;
  isCut: boolean;
  origin: { owner: ActionFlowOwner; blockId: string } | null;
  pendingPasteOwner: ActionFlowOwner | null;
  pendingPasteBlockId: string | null;
}

@Injectable({ providedIn: 'root' })
export class ActionClipboardService {
  private readonly _entry = signal<ActionClipboardEntry | null>(null);

  readonly entry = this._entry.asReadonly();

  readonly hasContent = computed(() => this._entry() !== null);

  readonly cutBlockId = computed(() => {
    const entry = this._entry();
    return entry?.isCut ? entry.origin?.blockId ?? null : null;
  });

  copy(block: ActionBlock): void {
    this._entry.set({
      block: structuredClone(block),
      isCut: false,
      origin: null,
      pendingPasteOwner: null,
      pendingPasteBlockId: null,
    });
  }

  cut(block: ActionBlock, owner: ActionFlowOwner): void {
    this._entry.set({
      block: structuredClone(block),
      isCut: true,
      origin: { owner, blockId: block.id },
      pendingPasteOwner: null,
      pendingPasteBlockId: null,
    });
  }

  markPasted(owner: ActionFlowOwner, blockId: string): void {
    this._entry.update(entry =>
      entry?.isCut ? { ...entry, pendingPasteOwner: owner, pendingPasteBlockId: blockId } : entry,
    );
  }

  clear(): void {
    this._entry.set(null);
  }
}

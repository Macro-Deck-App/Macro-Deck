import { store, ReadableStore, WritableStore } from '../store/store';
import { UiNode } from '../ui-framework/ui-node.interface';
import { applyUiPatch, UiPatch } from '../ui-framework/ui-patch';

interface SessionEntry {
  tree: UiNode;
  revision: number;
}

export class UiSessionStore {
  private readonly entries: { [sessionId: string]: SessionEntry } = {};
  private readonly revisionStore: WritableStore<number> = store(0);
  private listeners: Array<(sessionId: string | null) => void> = [];

  get changed(): ReadableStore<number> {
    return this.revisionStore;
  }

  onChange(listener: (sessionId: string | null) => void): () => void {
    this.listeners.push(listener);
    let live = true;
    return () => {
      if (!live) return;
      live = false;
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners = this.listeners.slice(0, at).concat(this.listeners.slice(at + 1));
    };
  }

  private announce(sessionId: string | null): void {
    const notified = this.listeners.slice();
    for (let index = 0; index < notified.length; index++) notified[index](sessionId);
  }

  tree(sessionId: string): UiNode | undefined {
    const entry = this.entries[sessionId];
    return entry ? entry.tree : undefined;
  }

  revision(sessionId: string): number | undefined {
    const entry = this.entries[sessionId];
    return entry ? entry.revision : undefined;
  }

  has(sessionId: string): boolean {
    return Object.prototype.hasOwnProperty.call(this.entries, sessionId);
  }

  treeUpdated(sessionId: string, revision: number, tree: UiNode): void {
    this.entries[sessionId] = { tree, revision };
    this.revisionStore.update(value => value + 1);
    this.announce(sessionId);
  }

  patched(sessionId: string, patch: UiPatch): boolean {
    const entry = this.entries[sessionId];
    if (!entry) return false;

    const next = applyUiPatch(entry.tree, entry.revision, patch);
    if (next === null) {
      this.invalidated(sessionId);
      return false;
    }

    this.entries[sessionId] = { tree: next, revision: patch.toRevision };
    this.revisionStore.update(value => value + 1);
    this.announce(sessionId);
    return true;
  }

  invalidated(sessionId: string): void {
    if (!this.has(sessionId)) return;
    delete this.entries[sessionId];
    this.revisionStore.update(value => value + 1);
    this.announce(sessionId);
  }

  clear(): void {
    let had = false;
    for (const sessionId in this.entries) {
      if (Object.prototype.hasOwnProperty.call(this.entries, sessionId)) {
        delete this.entries[sessionId];
        had = true;
      }
    }
    if (had) {
      this.revisionStore.update(value => value + 1);
      this.announce(null);
    }
  }
}

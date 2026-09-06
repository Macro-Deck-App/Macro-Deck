import { Injectable, effect, inject, signal } from '@angular/core';

import { UiConfigEvents, UiConfigPrimitives, UiConfigProperties, UiNode, nodeString, nodeText, nodeVisibleWhen } from '@macro-deck/runtime';
import { LocalizationService, UiNodeEventBus } from '@shared';
import { UI_CHROME_TYPES } from './ui-chrome-types.util';

export interface UiHoistedMessage {
  id: string;
  text: string;
}

@Injectable()
export class UiRenderContext {
  private readonly localization = inject(LocalizationService);
  private readonly eventBus = inject(UiNodeEventBus);

  private readonly nodesById = new Map<string, UiNode>();
  private readonly scopeById = new Map<string, string | undefined>();
  private readonly forIndex = new Map<string, UiHoistedMessage[]>();
  private currentRoot: UiNode | null = null;

  // forIndex itself is a plain Map, so reindexing it is invisible to Angular's reactivity - a
  // consumer's computed() only reruns on a signal it read changing, and `Map.get` isn't one. This
  // gives messagesFor() a signal to depend on, so it (and anything built on it, e.g. errorEntries and
  // aria-describedby) recomputes after a reindex instead of serving the stale entries it first read.
  private readonly forIndexVersion = signal(0);

  private readonly overlaySignal = signal<Record<string, unknown>>({});
  private readonly externalValuesSignal = signal<Record<string, unknown> | null>(null);
  private readonly disabledSignal = signal(false);
  private readonly scopeRefIdSignal = signal<string | undefined>(undefined);
  private readonly unsavedChangesSignal = signal(false);

  readonly events$ = this.eventBus.events$;

  constructor() {
    // The validation index below is a plain Map built by one imperative pass, not a computed() - so
    // unlike every other resolved-text signal in the renderer, it needs an explicit dependency on
    // the catalog to be rebuilt (not merely recomputed) when the culture or catalog changes, on a
    // surface that is already on screen (issue #326).
    effect(() => {
      this.localization.catalogVersion();
      if (this.currentRoot) this.reindex(this.currentRoot);
    });
  }

  setRoot(root: UiNode | null): void {
    this.overlaySignal.set({});
    this.currentRoot = root;
    this.reindex(root);
  }

  private reindex(root: UiNode | null): void {
    this.nodesById.clear();
    this.scopeById.clear();
    this.forIndex.clear();
    if (root) this.index(root, undefined);
    this.forIndexVersion.update(v => v + 1);
  }

  setValues(values: Record<string, unknown> | null): void {
    this.externalValuesSignal.set(values);
  }

  setDisabled(disabled: boolean): void {
    this.disabledSignal.set(disabled);
  }

  get disabled(): boolean {
    return this.disabledSignal();
  }

  setScopeRefId(scopeRefId: string | undefined): void {
    this.scopeRefIdSignal.set(scopeRefId);
  }

  get scopeRefId(): string | undefined {
    return this.scopeRefIdSignal();
  }

  setUnsavedChanges(unsavedChanges: boolean): void {
    this.unsavedChangesSignal.set(unsavedChanges);
  }

  get unsavedChanges(): boolean {
    return this.unsavedChangesSignal();
  }

  messagesFor(nodeId: string): UiHoistedMessage[] {
    this.forIndexVersion();
    return this.forIndex.get(nodeId) ?? [];
  }

  isHoistedOnto(forId: string): boolean {
    const target = this.nodesById.get(forId);
    return !!target && !UI_CHROME_TYPES.has(target.type);
  }

  isVisible(node: UiNode): boolean {
    const when = nodeVisibleWhen(node);
    if (!when) return true;

    const scopeId = this.scopeById.get(node.id);
    const target = (scopeId && this.nodesById.get(`${scopeId}.${when.parameterName}`))
      || this.nodesById.get(when.parameterName);
    if (!target) return true;

    const current = this.stringValue(this.getValue(target));
    return when.values.some(value => value.toLowerCase() === current.toLowerCase());
  }

  getValue(node: UiNode): unknown {
    const external = this.externalValuesSignal();
    if (external && Object.prototype.hasOwnProperty.call(external, node.id)) {
      return external[node.id];
    }

    const overlay = this.overlaySignal();
    if (Object.prototype.hasOwnProperty.call(overlay, node.id)) {
      return overlay[node.id];
    }

    const properties = node.properties ?? {};
    if (UiConfigProperties.Value in properties) return properties[UiConfigProperties.Value];
    return properties[UiConfigProperties.DefaultValue];
  }

  setValue(node: UiNode, value: unknown): void {
    if (this.externalValuesSignal() === null) {
      this.overlaySignal.update(current => ({ ...current, [node.id]: value }));
    }
    this.emit(node, UiConfigEvents.Change, value);
  }

  clearValue(nodeId: string): void {
    this.overlaySignal.update(current => {
      if (!(nodeId in current)) return current;
      const next = { ...current };
      delete next[nodeId];
      return next;
    });
  }

  emit(node: UiNode, name: string, data?: unknown): void {
    this.eventBus.emit(node, name, data);
  }

  clearDescendantInputs(rootId: string): void {
    const root = this.nodesById.get(rootId);
    if (!root) return;

    const visit = (node: UiNode): void => {
      const isContainer = node.type === UiConfigPrimitives.Object || node.type === UiConfigPrimitives.Array
        || node.type === UiConfigPrimitives.Stack || node.type === UiConfigPrimitives.AdvancedSection
        || node.type === UiConfigPrimitives.Step || node.type === UiConfigPrimitives.Flow
        || node.type === UiConfigPrimitives.Tabs || node.type === UiConfigPrimitives.Tab;

      if (!isContainer) {
        this.clearValue(node.id);
        this.emit(node, UiConfigEvents.Change, null);
      }
      for (const child of node.children ?? []) visit(child);
    };
    for (const child of root.children ?? []) visit(child);
  }

  private stringValue(value: unknown): string {
    return value === undefined || value === null ? '' : String(value);
  }

  private index(node: UiNode, scopeId: string | undefined): void {
    this.nodesById.set(node.id, node);
    this.scopeById.set(node.id, scopeId);

    const childScope = node.type === UiConfigPrimitives.Object || node.type === UiConfigPrimitives.Array
      ? node.id
      : scopeId;
    for (const child of node.children ?? []) this.index(child, childScope);
    if (node.fallback) this.index(node.fallback, scopeId);

    if (node.type === UiConfigPrimitives.ValidationMessage) {
      const forId = nodeString(node, UiConfigProperties.For);
      if (forId) {
        const text = nodeText(node, UiConfigProperties.Text, this.localization) ?? '';
        const list = this.forIndex.get(forId) ?? [];
        list.push({ id: node.id, text });
        this.forIndex.set(forId, list);
      }
    }
  }
}

import { Injectable, Signal, WritableSignal, inject, signal, untracked, computed } from '@angular/core';
import { BindCatalogVariableRequest, BindCatalogVariableResponse, RenameCatalogVariableRequest, RenameCatalogVariableResponse, ResolveCatalogVariableResponse, UnbindCatalogVariableResponse, VariableCatalogNode, VariableCatalogProvider, VariablesChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import type { Variable } from '@macro-deck/runtime';

export interface VariableCatalogPage {
  nodes: VariableCatalogNode[];
  nextCursor?: string;
  hasMore: boolean;
  available: boolean;
}

const EMPTY_PAGE: VariableCatalogPage = { nodes: [], hasMore: false, available: true };

@Injectable({ providedIn: 'root' })
export class VariableCatalogService {
  private readonly api = inject(ApiService);

  readonly providers = signal<VariableCatalogProvider[]>([]);
  private loadedProviders = false;
  private loadingProviders: Promise<void> | null = null;

  private readonly pages = new Map<string, WritableSignal<VariableCatalogPage>>();

  private readonly seenLeaves = signal<ReadonlyMap<string, ReadonlyMap<string, boolean>>>(new Map());
  private readonly loadedKeys = new Set<string>();
  private readonly loadingKeys = new Set<string>();

  constructor() {
    this.subscribeToEvents();
  }

  async loadProviders(): Promise<void> {
    if (this.loadingProviders) {
      return this.loadingProviders;
    }
    this.loadingProviders = this.runLoadProviders().finally(() => {
      this.loadingProviders = null;
    });
    return this.loadingProviders;
  }

  private async runLoadProviders(): Promise<void> {
    try {
      const response = await this.api.getVariableCatalogProviders();
      this.providers.set(response?.providers ?? []);
      this.loadedProviders = true;
    } catch (error) {
      console.error('Failed to load variable catalog providers:', error);
    }
  }

  providersFor(): Signal<VariableCatalogProvider[]> {
    if (!this.loadedProviders && !this.loadingProviders) {
      untracked(() => void this.loadProviders());
    }
    return this.providers.asReadonly();
  }

  pageFor(integrationId: string, parentId: string | undefined, search: string | undefined): Signal<VariableCatalogPage> {
    const key = VariableCatalogService.key(integrationId, parentId, search);
    const page = this.pageSignal(key);
    if (!this.loadedKeys.has(key) && !this.loadingKeys.has(key)) {
      untracked(() => void this.load(integrationId, parentId, search));
    }
    return page.asReadonly();
  }

  isLoaded(integrationId: string, parentId: string | undefined, search: string | undefined): boolean {
    return this.loadedKeys.has(VariableCatalogService.key(integrationId, parentId, search));
  }

  async reload(integrationId: string, parentId: string | undefined, search: string | undefined): Promise<void> {
    const key = VariableCatalogService.key(integrationId, parentId, search);
    this.loadedKeys.delete(key);
    await this.load(integrationId, parentId, search);
  }

  private async load(integrationId: string, parentId: string | undefined, search: string | undefined): Promise<void> {
    const key = VariableCatalogService.key(integrationId, parentId, search);
    if (this.loadingKeys.has(key) || this.loadedKeys.has(key)) {
      return;
    }
    this.loadingKeys.add(key);
    try {
      const response = await this.api.discoverCatalogVariables({ integrationId, parentId, search: search || undefined });
      this.pageSignal(key).set(response
        ? { nodes: response.nodes, nextCursor: response.nextCursor, hasMore: response.hasMore, available: response.available }
        : { nodes: [], hasMore: false, available: false });
      this.recordLeaves(integrationId, response?.nodes ?? []);
      this.loadedKeys.add(key);
    } catch (error) {
      console.error(`Failed to discover catalog variables for ${integrationId}:`, error);
      this.pageSignal(key).set({ nodes: [], hasMore: false, available: false });
      this.loadedKeys.add(key);
    } finally {
      this.loadingKeys.delete(key);
    }
  }

  unboundCountFor(integrationId: string): number | null {
    const reported = this.providersFor()().find(p => p.integrationId === integrationId)?.unboundCount;
    if (reported !== undefined && reported !== null) {
      return reported;
    }

    let unbound = 0;
    for (const bound of (this.seenLeaves().get(integrationId) ?? new Map()).values()) {
      if (!bound) {
        unbound++;
      }
    }
    return unbound;
  }

  async loadMore(integrationId: string, parentId: string | undefined, search: string | undefined): Promise<void> {
    const key = VariableCatalogService.key(integrationId, parentId, search);
    const current = this.pageSignal(key)();
    if (!current.hasMore || this.loadingKeys.has(key)) {
      return;
    }
    this.loadingKeys.add(key);
    try {
      const response = await this.api.discoverCatalogVariables({
        integrationId,
        parentId,
        search: search || undefined,
        cursor: current.nextCursor,
      });
      if (!response) {
        return;
      }
      this.pageSignal(key).update(page => ({
        nodes: [...page.nodes, ...response.nodes],
        nextCursor: response.nextCursor,
        hasMore: response.hasMore,
        available: response.available,
      }));
    } catch (error) {
      console.error(`Failed to load more catalog variables for ${integrationId}:`, error);
    } finally {
      this.loadingKeys.delete(key);
    }
  }

  async resolve(integrationId: string, resourceId: string): Promise<ResolveCatalogVariableResponse | null> {
    return this.api.resolveCatalogVariable({ integrationId, resourceId });
  }

  async bind(request: BindCatalogVariableRequest): Promise<BindCatalogVariableResponse> {
    const response = await this.api.bindCatalogVariable(request);
    if (response.variable) {
      this.invalidateIntegration(request.integrationId);
    }
    return response;
  }

  async unbind(integrationId: string, variableId: string): Promise<UnbindCatalogVariableResponse> {
    const response = await this.api.unbindCatalogVariable({ variableId });
    if (response.success) {
      this.invalidateIntegration(integrationId);
    }
    return response;
  }

  async rename(request: RenameCatalogVariableRequest): Promise<RenameCatalogVariableResponse> {
    return this.api.renameCatalogVariable(request);
  }

  invalidateIntegration(integrationId: string): void {
    const prefix = `${integrationId}::`;
    for (const key of [...this.pages.keys()]) {
      if (key.startsWith(prefix)) {
        this.pages.delete(key);
        this.loadedKeys.delete(key);
        this.loadingKeys.delete(key);
      }
    }
  }

  private recordLeaves(integrationId: string, nodes: readonly VariableCatalogNode[]): void {
    const next = new Map(this.seenLeaves());
    const forIntegration = new Map(next.get(integrationId) ?? []);
    let changed = false;

    for (const node of nodes) {
      if (node.hasChildren || !node.type) {
        continue;
      }
      const bound = node.boundVariableId !== undefined && node.boundVariableId !== null;
      if (forIntegration.get(node.id) !== bound) {
        forIntegration.set(node.id, bound);
        changed = true;
      }
    }

    if (changed) {
      next.set(integrationId, forIntegration);
      this.seenLeaves.set(next);
    }
  }

  private pageSignal(key: string): WritableSignal<VariableCatalogPage> {
    let page = this.pages.get(key);
    if (!page) {
      page = signal<VariableCatalogPage>(EMPTY_PAGE);
      this.pages.set(key, page);
    }
    return page;
  }

  private static key(integrationId: string, parentId: string | undefined, search: string | undefined): string {
    return `${integrationId}::${parentId ?? ''}::${(search ?? '').trim()}`;
  }

  private subscribeToEvents(): void {
    this.api.onNotification<VariablesChangedEvent>('VariablesChangedEvent').subscribe(event => {
      const integrationIds = new Set<string>();
      for (const variable of event.upserted) {
        if (variable.dynamicResourceId && variable.ownerIntegrationId) {
          integrationIds.add(variable.ownerIntegrationId);
        }
      }
      for (const integrationId of integrationIds) {
        this.invalidateIntegration(integrationId);
      }
    });
  }
}

export function resolveBoundVariable(node: VariableCatalogNode, variables: Variable[]): Variable | null {
  if (!node.boundVariableId) {
    return null;
  }
  return variables.find(v => v.id === node.boundVariableId) ?? null;
}

import { Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { resolveLocalizedText } from '@macro-deck/runtime';
import type { VariableCatalogNode, VariableType } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { VariableCatalogService } from '../../services/variable-catalog.service';

export type VariableCatalogRow =
  | { kind: 'catalog-leaf'; key: string; integrationId: string; node: VariableCatalogNode; depth: number }
  | { kind: 'catalog-branch'; key: string; integrationId: string; node: VariableCatalogNode; depth: number; expanded: boolean }
  | { kind: 'catalog-note'; key: string; note: 'loading' | 'not-bindable'; depth: number }
  | { kind: 'catalog-more'; key: string; integrationId: string; parentId: string | undefined; depth: number }
  | { kind: 'catalog-grow'; key: string; integrationId: string };

export type VariableCatalogState = 'loading' | 'offline' | 'empty' | 'ready';

interface VariableCatalogSection {
  rows: VariableCatalogRow[];
  state: VariableCatalogState;
  complete: boolean;
  truncated: boolean;
  entryCount: number;
}

export interface VariableCatalogGroup {
  integrationId: string;
  expanded: boolean;
  collapsible: boolean;
  count: number | null;
  state: VariableCatalogState;
  rows: VariableCatalogRow[];
}

export interface VariableCatalogRowsOptions {
  integrationIds: Signal<readonly string[]>;
  search: Signal<string>;
  acceptedTypes?: Signal<readonly VariableType[]>;
  writableOnly?: Signal<boolean>;
  isExpanded: (integrationId: string) => boolean;
}

export interface VariableCatalogRows {
  searching: Signal<boolean>;
  group(integrationId: string): VariableCatalogGroup | null;
  isBindable(node: VariableCatalogNode): boolean;
  reference(node: VariableCatalogNode): string | null;
  displayName(node: VariableCatalogNode): string;
  toggleBranch(integrationId: string, node: VariableCatalogNode): void;
  loadMore(integrationId: string, parentId: string | undefined): void;
  grow(integrationId: string): void;
  retry(integrationId: string): void;
}

const PAGE_ENTRIES = 100;

const QUERY_DEBOUNCE_MS = 250;

const INACTIVE_SECTION: VariableCatalogSection = { rows: [], state: 'ready', complete: false, truncated: false, entryCount: 0 };

interface CatalogRequest {
  integrationId: string;
  parentId: string | undefined;
  search: string | undefined;
  more: boolean;
  priority: boolean;
}

interface Walk {
  integrationId: string;
  rows: VariableCatalogRow[];
  requests: CatalogRequest[];
  entries: number;
  budget: number;
  truncated: boolean;
  offline: boolean;
}

function normalizeQuery(search: string): string {
  return search.trim().replace(/^vars\./i, '');
}

export function createVariableCatalogRows(options: VariableCatalogRowsOptions): VariableCatalogRows {
  const catalog = inject(VariableCatalogService);
  const localization = inject(LocalizationService);

  const budgets = signal<ReadonlyMap<string, number>>(new Map());
  const expandedBranches = signal<ReadonlySet<string>>(new Set());
  const hostQuery = signal('');

  effect(onCleanup => {
    const query = normalizeQuery(options.search());
    const timer = setTimeout(() => hostQuery.set(query), QUERY_DEBOUNCE_MS);
    onCleanup(() => clearTimeout(timer));
  });

  const searching = computed(() => options.search().trim().length > 0);

  const searchesOnHost = (integrationId: string): boolean =>
    catalog.providersFor()().find(p => p.integrationId === integrationId)?.supportsSearch === true;

  const displayName = (node: VariableCatalogNode): string =>
    resolveLocalizedText(node.displayName, localization) || node.name;

  const reference = (node: VariableCatalogNode): string | null =>
    node.suggestedName ? `vars.${node.suggestedName}` : null;

  const isBindable = (node: VariableCatalogNode): boolean => {
    if (!node.type || node.boundVariableId) {
      return false;
    }
    if (options.writableOnly?.() && node.canWrite !== true) {
      return false;
    }
    const accepted = options.acceptedTypes?.() ?? [];
    return accepted.length === 0 || accepted.includes(node.type);
  };

  const matchesSearch = (node: VariableCatalogNode): boolean => {
    const query = options.search().toLowerCase().trim();
    return query.length === 0
      || (reference(node) ?? '').toLowerCase().includes(query)
      || displayName(node).toLowerCase().includes(query);
  };

  const branchKey = (integrationId: string, nodeId: string): string => `${integrationId}::${nodeId}`;

  const full = (walk: Walk): boolean => walk.entries >= walk.budget;

  const appendLeaf = (walk: Walk, node: VariableCatalogNode, depth: number): void => {
    walk.rows.push({ kind: 'catalog-leaf', key: `cat:${walk.integrationId}:${node.id}`, integrationId: walk.integrationId, node, depth });
    if (depth === 0) {
      walk.entries++;
    }
  };

  const appendFlat = (walk: Walk, parentId: string | undefined): void => {
    if (full(walk)) {
      walk.truncated = true;
      return;
    }

    if (!catalog.isLoaded(walk.integrationId, parentId, undefined)) {
      walk.requests.push({ integrationId: walk.integrationId, parentId, search: undefined, more: false, priority: false });
      return;
    }

    const page = catalog.pageFor(walk.integrationId, parentId, undefined)();
    if (!page.available) {
      walk.offline ||= parentId === undefined;
      return;
    }

    for (const node of page.nodes) {
      if (node.hasChildren) {
        appendFlat(walk, node.id);
      }
      if (isBindable(node) && matchesSearch(node)) {
        appendLeaf(walk, node, 0);
      }
      if (full(walk)) {
        walk.truncated = true;
        return;
      }
    }

    if (page.hasMore) {
      walk.requests.push({ integrationId: walk.integrationId, parentId, search: undefined, more: true, priority: false });
    }
  };

  const appendBranch = (walk: Walk, node: VariableCatalogNode, depth: number): void => {
    const key = branchKey(walk.integrationId, node.id);
    const expanded = expandedBranches().has(key);
    walk.rows.push({ kind: 'catalog-branch', key: `branch:${key}`, integrationId: walk.integrationId, node, depth, expanded });
    if (depth === 0) {
      walk.entries++;
    }
    if (!expanded) {
      return;
    }

    const childDepth = depth + 1;
    if (!catalog.isLoaded(walk.integrationId, node.id, undefined)) {
      walk.requests.push({ integrationId: walk.integrationId, parentId: node.id, search: undefined, more: false, priority: true });
      walk.rows.push({ kind: 'catalog-note', key: `loading:${key}`, note: 'loading', depth: childDepth });
      return;
    }

    const page = catalog.pageFor(walk.integrationId, node.id, undefined)();
    const before = walk.rows.length;
    for (const child of page.nodes) {
      if (child.hasChildren) {
        appendBranch(walk, child, childDepth);
      } else if (isBindable(child)) {
        appendLeaf(walk, child, childDepth);
      }
    }

    if (page.hasMore) {
      walk.rows.push({ kind: 'catalog-more', key: `more:${key}`, integrationId: walk.integrationId, parentId: node.id, depth: childDepth });
    } else if (walk.rows.length === before) {
      walk.rows.push({ kind: 'catalog-note', key: `empty:${key}`, note: 'not-bindable', depth: childDepth });
    }
  };

  // The search goes only on root pages: a provider may answer a searched query from anywhere in its
  // tree, so a searched child request would not return that node's children.
  const appendSearchable = (walk: Walk, firstPagePriority: boolean): void => {
    const search = hostQuery() || undefined;
    if (!catalog.isLoaded(walk.integrationId, undefined, search)) {
      walk.requests.push({ integrationId: walk.integrationId, parentId: undefined, search, more: false, priority: firstPagePriority });
      return;
    }

    const page = catalog.pageFor(walk.integrationId, undefined, search)();
    if (!page.available) {
      walk.offline = true;
      return;
    }

    for (const node of page.nodes) {
      if (full(walk)) {
        walk.truncated = true;
        return;
      }
      if (node.hasChildren) {
        appendBranch(walk, node, 0);
      } else if (isBindable(node)) {
        appendLeaf(walk, node, 0);
      }
    }

    if (page.hasMore) {
      walk.requests.push({ integrationId: walk.integrationId, parentId: undefined, search, more: true, priority: false });
    }
  };

  const walks = computed<Walk[]>(() => {
    catalog.revision();
    const result: Walk[] = [];
    for (const integrationId of options.integrationIds()) {
      const walk: Walk = {
        integrationId,
        rows: [],
        requests: [],
        entries: 0,
        budget: budgets().get(integrationId) ?? PAGE_ENTRIES,
        truncated: false,
        offline: false,
      };
      if (searchesOnHost(integrationId)) {
        const active = options.isExpanded(integrationId) || hostQuery().length > 0;
        if (active) {
          appendSearchable(walk, true);
        }
      } else {
        appendFlat(walk, undefined);
      }
      result.push(walk);
    }
    return result;
  });

  const sections = computed<ReadonlyMap<string, VariableCatalogSection>>(() => {
    const queryPending = normalizeQuery(options.search()) !== hostQuery();
    const map = new Map<string, VariableCatalogSection>();
    for (const walk of walks()) {
      const onHost = searchesOnHost(walk.integrationId);
      const active = !onHost || options.isExpanded(walk.integrationId) || hostQuery().length > 0;
      if (!active && !queryPending) {
        map.set(walk.integrationId, INACTIVE_SECTION);
        continue;
      }

      const pending = walk.requests.length > 0 || (onHost && queryPending);
      const state: VariableCatalogState = walk.rows.length > 0
        ? 'ready'
        : pending ? 'loading' : walk.offline ? 'offline' : 'empty';
      map.set(walk.integrationId, {
        rows: walk.rows,
        state,
        complete: !walk.truncated && walk.requests.length === 0 && !pending,
        truncated: walk.truncated || (full(walk) && walk.requests.length > 0),
        entryCount: walk.entries,
      });
    }
    return map;
  });

  effect(() => {
    const all = walks();
    const next = all.flatMap(w => w.requests).find(r => r.priority)
      ?? all.find(w => w.requests.length > 0 && !full(w))?.requests[0];
    if (!next) {
      return;
    }

    untracked(() => {
      void (next.more
        ? catalog.loadMore(next.integrationId, next.parentId, next.search)
        : Promise.resolve(catalog.pageFor(next.integrationId, next.parentId, next.search)()));
    });
  });

  const unboundCount = (integrationId: string, section: VariableCatalogSection, onHost: boolean): number | null => {
    if (options.writableOnly?.() || (options.acceptedTypes?.() ?? []).length > 0) {
      return null;
    }
    if (!onHost && section.complete) {
      return section.entryCount;
    }
    return catalog.providersFor()().find(p => p.integrationId === integrationId)?.unboundCount ?? null;
  };

  const listed = (integrationId: string, section: VariableCatalogSection): VariableCatalogRow[] =>
    section.truncated
      ? [...section.rows, { kind: 'catalog-grow', key: `grow:${integrationId}`, integrationId }]
      : section.rows;

  const group = (integrationId: string): VariableCatalogGroup | null => {
    const section = sections().get(integrationId);
    if (!section) {
      return null;
    }

    const onHost = searchesOnHost(integrationId);
    const isSearching = searching();
    const visible = isSearching
      ? section.state === 'loading' || section.rows.length > 0
      : onHost || section.state !== 'empty';
    if (!visible) {
      return null;
    }

    const expanded = isSearching || options.isExpanded(integrationId);
    return {
      integrationId,
      expanded,
      collapsible: !isSearching,
      count: isSearching ? null : unboundCount(integrationId, section, onHost),
      state: section.state,
      rows: expanded && section.state === 'ready' ? listed(integrationId, section) : [],
    };
  };

  return {
    searching,
    group,
    isBindable,
    reference,
    displayName,
    toggleBranch(integrationId, node) {
      const key = branchKey(integrationId, node.id);
      expandedBranches.update(set => {
        const next = new Set(set);
        if (!next.delete(key)) {
          next.add(key);
        }
        return next;
      });
    },
    loadMore(integrationId, parentId) {
      void catalog.loadMore(integrationId, parentId, undefined);
    },
    grow(integrationId) {
      const walk = untracked(walks).find(w => w.integrationId === integrationId);
      if (!walk || !full(walk)) {
        return;
      }
      budgets.update(map => {
        const next = new Map(map);
        next.set(integrationId, (map.get(integrationId) ?? PAGE_ENTRIES) + PAGE_ENTRIES);
        return next;
      });
    },
    retry(integrationId) {
      const search = searchesOnHost(integrationId) ? hostQuery() || undefined : undefined;
      void catalog.reload(integrationId, undefined, search);
    },
  };
}

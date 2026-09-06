import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { DiscoverCatalogVariablesRequest, DiscoverCatalogVariablesResponse, VariableCatalogNode, VariableCatalogProvider } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { VariableCatalogTreeComponent } from './variable-catalog-tree.component';

function node(overrides: Partial<VariableCatalogNode>): VariableCatalogNode {
  return {
    id: 'id',
    name: 'name',
    displayName: 'name',
    hasChildren: false,
    ...overrides,
  };
}

function page(nodes: VariableCatalogNode[], opts: Partial<DiscoverCatalogVariablesResponse> = {}): DiscoverCatalogVariablesResponse {
  return { nodes, hasMore: false, available: true, ...opts };
}

function leaves(count: number, offset = 0): VariableCatalogNode[] {
  return Array.from({ length: count }, (_, i) => node({ id: `n${offset + i}`, name: `n${offset + i}`, displayName: `n${offset + i}` }));
}

function rowNodeIds(rows: { kind: string; node?: VariableCatalogNode }[]): (string | null)[] {
  return rows.map(r => r.node?.id ?? null);
}

describe('VariableCatalogTreeComponent', () => {
  let fixture: ComponentFixture<VariableCatalogTreeComponent>;
  let component: VariableCatalogTreeComponent;
  let discoverSpy: jasmine.Spy;
  let bindSpy: jasmine.Spy;

  function setup(
    discoverImpl: (req: DiscoverCatalogVariablesRequest) => Promise<DiscoverCatalogVariablesResponse | null>,
    providers: VariableCatalogProvider[] = [],
  ): void {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'discoverCatalogVariables',
      'bindCatalogVariable',
      'getVariables',
      'getVariableCatalogProviders',
      'onNotification',
    ]);
    apiSpy.discoverCatalogVariables.and.callFake(discoverImpl);
    apiSpy.bindCatalogVariable.and.resolveTo({ variable: undefined });
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getVariableCatalogProviders.and.resolveTo({ providers });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    discoverSpy = apiSpy.discoverCatalogVariables;
    bindSpy = apiSpy.bindCatalogVariable;

    TestBed.configureTestingModule({
      imports: [VariableCatalogTreeComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(VariableCatalogTreeComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('integrationId', 'home-assistant');
    fixture.componentRef.setInput('integrationName', 'Home Assistant');
  }

  it('debounces a typing burst into a single discovery request', async () => {
    setup(() => Promise.resolve(page(leaves(2))));
    fixture.detectChanges();
    await fixture.whenStable();
    const callsBeforeTyping = discoverSpy.calls.count();

    jasmine.clock().install();
    try {
      component.onSearchChange('h');
      component.onSearchChange('ho');
      component.onSearchChange('hom');
      component.onSearchChange('home');
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();

    expect(discoverSpy.calls.count() - callsBeforeTyping).toBe(1);
    expect(discoverSpy.calls.mostRecent().args[0].search).toBe('home');
  });

  it('loads a branch\'s children once on expand, and re-expanding after collapse hits the cache', async () => {
    const branch = node({ id: 'b1', name: 'Living Room', displayName: 'Living Room', hasChildren: true });
    setup(req => Promise.resolve(
      req.parentId === 'b1' ? page(leaves(2)) : page([branch]),
    ));
    fixture.detectChanges();
    await fixture.whenStable();

    const childCallsFor = () => discoverSpy.calls.allArgs().filter(args => args[0].parentId === 'b1').length;

    component.toggleBranch(branch);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(childCallsFor()).toBe(1);

    // Not just that the service was asked - the children must actually render (regression: a build
    // that only flipped the disclosure arrow without rendering rows passed every existing test).
    expect(rowNodeIds(component.rows())).toEqual(['b1', 'n0', 'n1']);
    const rendered = (fixture.nativeElement as HTMLElement).querySelectorAll('.dvt-leaf, .dvt-branch');
    expect(rendered.length).toBe(3);

    component.toggleBranch(branch); // collapse
    component.toggleBranch(branch); // re-expand
    await fixture.whenStable();

    expect(childCallsFor()).toBe(1);
  });

  it('never expands a branch during a search, so a search+ParentId-ignoring provider cannot recurse', async () => {
    // Home Assistant (and any provider that ignores parentId once a search is present, per its own
    // DiscoverAsync contract) returns the exact same flat match set regardless of parentId - so a
    // matched node that also appears as its own "child" must never be treated as expandable while
    // searching, or appendRows recurses into itself without ever terminating (issue #760 defect #2).
    const selfMatch = node({ id: 'self', name: 'self', displayName: 'self', hasChildren: true });
    setup(req => Promise.resolve(
      req.search ? page([selfMatch]) : req.parentId === 'self' ? page(leaves(2)) : page([selfMatch]),
    ));
    fixture.detectChanges();
    await fixture.whenStable();

    // Expanding in ordinary browse mode still has to work (this is the other half of defect #2).
    component.toggleBranch(selfMatch);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(rowNodeIds(component.rows())).toEqual(['self', 'n0', 'n1']);

    // Now search - the expand state resets (documented, both transitions), and the same self-match
    // shape from the provider must render as an inert, collapsed row rather than recursing.
    jasmine.clock().install();
    try {
      component.onSearchChange('self');
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();

    expect(() => component.toggleBranch(selfMatch)).not.toThrow();
    expect(() => component.rows()).not.toThrow();
    fixture.detectChanges();
    expect(rowNodeIds(component.rows())).toEqual(['self']);
    expect(fixture.nativeElement.querySelector('.dvt-branch[disabled]')).toBeTruthy();

    // Clearing the search afterwards must still restore the full root list (issue #760 defect #3).
    jasmine.clock().install();
    try {
      component.onSearchChange('');
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rowNodeIds(component.rows())).toEqual(['self']);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.dvt-leaf, .dvt-branch').length).toBe(1);
  });

  it('appends pages on load-more without duplicating any node, 100 -> 200 -> 250', async () => {
    setup(req => {
      const cursor = req.cursor;
      if (!cursor) return Promise.resolve(page(leaves(100, 0), { hasMore: true, nextCursor: 'page2' }));
      if (cursor === 'page2') return Promise.resolve(page(leaves(100, 100), { hasMore: true, nextCursor: 'page3' }));
      if (cursor === 'page3') return Promise.resolve(page(leaves(50, 200), { hasMore: false }));
      return Promise.resolve(page([]));
    });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.rows().filter(r => r.kind === 'leaf').length).toBe(100);

    component.loadMore(undefined);
    await fixture.whenStable();
    expect(component.rows().filter(r => r.kind === 'leaf').length).toBe(200);

    component.loadMore(undefined);
    await fixture.whenStable();

    const leafRows = component.rows().filter(r => r.kind === 'leaf');
    expect(leafRows.length).toBe(250);
    expect(new Set(leafRows.map(r => r.key)).size).toBe(250);
  });

  it('replaces the browsed tree with search results, and clearing search restores the tree', async () => {
    const browsed = leaves(2, 0); // n0, n1
    const searched = [node({ id: 'match', name: 'match', displayName: 'match' })];
    setup(req => Promise.resolve(req.search ? page(searched) : page(browsed)));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.rows().map(r => (r.kind === 'leaf' ? r.node.id : null))).toEqual(['n0', 'n1']);

    jasmine.clock().install();
    try {
      component.onSearchChange('match');
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();

    expect(component.rows().map(r => (r.kind === 'leaf' ? r.node.id : null))).toEqual(['match']);

    jasmine.clock().install();
    try {
      component.onSearchChange('');
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();

    expect(component.rows().map(r => (r.kind === 'leaf' ? r.node.id : null))).toEqual(['n0', 'n1']);
  });

  it('clearing the rendered search box (not just the signal) restores the rendered root list', async () => {
    // Drives the real `<input>` the way a user's keystrokes or the native search-cancel button do,
    // rather than calling onSearchChange() directly - a regression here is exactly the class of bug
    // that a call-the-method-directly test cannot catch (issue #760 defect #3).
    const browsed = leaves(2, 0);
    const searched = [node({ id: 'match', name: 'match', displayName: 'match' })];
    setup(req => Promise.resolve(req.search ? page(searched) : page(browsed)));
    fixture.detectChanges();
    await fixture.whenStable();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('.dvt-search input');
    expect(input).toBeTruthy();

    jasmine.clock().install();
    try {
      input.value = 'match';
      input.dispatchEvent(new Event('input'));
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('match');

    jasmine.clock().install();
    try {
      input.value = '';
      input.dispatchEvent(new Event('input'));
      jasmine.clock().tick(300);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    const rendered = (fixture.nativeElement as HTMLElement).querySelectorAll('.dvt-leaf, .dvt-branch');
    expect(Array.from(rendered).map(el => el.textContent?.trim())).toEqual(['n0', 'n1']);
  });

  it('distinguishes an unreachable provider from a genuinely empty one', async () => {
    setup(() => Promise.resolve(page([], { available: false })));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.available()).toBeFalse();
    expect(component.isEmpty()).toBeFalse();

    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('shared-empty-state')?.textContent).toContain('Couldn\'t connect');
  });

  it('shows the empty state (not the offline state) when the provider answers with zero resources', async () => {
    setup(() => Promise.resolve(page([], { available: true })));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.available()).toBeTrue();
    expect(component.isEmpty()).toBeTrue();

    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('shared-empty-state')?.textContent).toContain('No resources');
  });

  it('retry re-requests after an offline response', async () => {
    setup(() => Promise.resolve(page([], { available: false })));
    fixture.detectChanges();
    await fixture.whenStable();
    const callsBeforeRetry = discoverSpy.calls.count();

    component.retry();
    await fixture.whenStable();

    expect(discoverSpy.calls.count()).toBeGreaterThan(callsBeforeRetry);
  });

  it('in pick mode, binds an unbound leaf and emits the resulting variable', async () => {
    const leaf = node({ id: 'r1', name: 'r1', displayName: 'r1', type: 'text' });
    setup(() => Promise.resolve(page([leaf])));
    fixture.componentRef.setInput('mode', 'pick');
    fixture.detectChanges();
    await fixture.whenStable();

    const bound: Variable = {
      id: 'v1', name: 'r1', scope: 'global', type: 'text', classification: 'integration',
      ownerIntegrationId: 'home-assistant', value: '', dynamicResourceId: 'r1',
    };
    bindSpy.and.resolveTo({ variable: bound });

    const picked: Variable[] = [];
    component.pick.subscribe(v => picked.push(v));

    component.activateLeaf(leaf);
    await fixture.whenStable();

    expect(bindSpy).toHaveBeenCalledWith({ integrationId: 'home-assistant', resourceId: 'r1', type: 'text' });
    expect(picked).toEqual([bound]);
  });

  it('refuses a leaf outside the accepted types before it is ever bound', async () => {
    // The refusal has to happen ahead of the bind: binding first and filtering afterwards leaves a
    // stray variable behind on every leaf the caller then declines to use.
    const textLeaf = node({ id: 'r1', name: 'r1', displayName: 'r1', type: 'text', canWrite: true });
    setup(() => Promise.resolve(page([textLeaf])));
    fixture.componentRef.setInput('mode', 'pick');
    fixture.componentRef.setInput('acceptedTypes', ['numeric']);
    fixture.detectChanges();
    await fixture.whenStable();

    const picked: Variable[] = [];
    component.pick.subscribe(v => picked.push(v));

    component.activateLeaf(textLeaf);
    await fixture.whenStable();

    expect(component.isBindable(textLeaf)).toBeFalse();
    expect(bindSpy).not.toHaveBeenCalled();
    expect(picked).toEqual([]);
  });

  it('refuses a read-only leaf under writableOnly before it is ever bound', async () => {
    const readOnly = node({ id: 'r1', name: 'r1', displayName: 'r1', type: 'numeric' });
    const writable = node({ id: 'r2', name: 'r2', displayName: 'r2', type: 'numeric', canWrite: true });
    setup(() => Promise.resolve(page([readOnly, writable])));
    fixture.componentRef.setInput('mode', 'pick');
    fixture.componentRef.setInput('acceptedTypes', ['numeric']);
    fixture.componentRef.setInput('writableOnly', true);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.isBindable(readOnly)).toBeFalse();
    expect(component.isBindable(writable)).toBeTrue();

    component.activateLeaf(readOnly);
    await fixture.whenStable();

    expect(bindSpy).not.toHaveBeenCalled();
  });

  it('in browse mode, activating an unbound leaf asks the host to open the bind dialog rather than binding directly', async () => {
    const leaf = node({ id: 'r1', name: 'r1', displayName: 'r1', type: 'text' });
    setup(() => Promise.resolve(page([leaf])));
    fixture.detectChanges();
    await fixture.whenStable();

    const requested: VariableCatalogNode[] = [];
    component.bindRequested.subscribe(n => requested.push(n));

    component.activateLeaf(leaf);

    expect(requested).toEqual([leaf]);
    expect(bindSpy).not.toHaveBeenCalled();
  });

  it('re-queries and re-renders when integrationId changes on a live instance (regression: signal input)', async () => {
    setup(req => Promise.resolve(
      req.integrationId === 'home-assistant' ? page(leaves(1, 0)) : page(leaves(1, 100)),
    ));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.rows().map(r => (r.kind === 'leaf' ? r.node.id : null))).toEqual(['n0']);

    // Simulates the host reusing this component instance across a provider switch, as the two real
    // call sites do (`@if (dynamicSourceIntegrationId(); as integrationId)` stays truthy and never
    // recreates the component when only the id changes).
    fixture.componentRef.setInput('integrationId', 'adb');
    fixture.componentRef.setInput('integrationName', 'ADB');
    await fixture.whenStable();

    expect(discoverSpy.calls.mostRecent().args[0].integrationId).toBe('adb');
    expect(component.rows().map(r => (r.kind === 'leaf' ? r.node.id : null))).toEqual(['n100']);
  });

  it('names the offline provider correctly after a switch (regression: signal input)', async () => {
    setup(() => Promise.resolve(page([], { available: false })));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.offlineMessage()).toContain('Home Assistant');

    fixture.componentRef.setInput('integrationId', 'adb');
    fixture.componentRef.setInput('integrationName', 'ADB');
    await fixture.whenStable();

    expect(component.offlineMessage()).toContain('ADB');
    expect(component.offlineMessage()).not.toContain('Home Assistant');
  });

  it('hides the search box for a provider that does not support it', async () => {
    setup(() => Promise.resolve(page(leaves(1))), [
      { integrationId: 'home-assistant', name: 'Home Assistant', supportsManualIds: false, supportsSearch: false },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.dvt-search')).toBeNull();
  });

  it('shows the manual id input only when the provider reports supportsManualIds', async () => {
    setup(() => Promise.resolve(page(leaves(1))), [
      { integrationId: 'home-assistant', name: 'Home Assistant', supportsManualIds: true, supportsSearch: true },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('shared-variable-catalog-id-input')).toBeTruthy();
  });

  it('surfaces (rather than swallows) a failed pick-mode bind', async () => {
    const leaf = node({ id: 'r1', name: 'r1', displayName: 'r1', type: 'text' });
    setup(() => Promise.resolve(page([leaf])));
    fixture.componentRef.setInput('mode', 'pick');
    fixture.detectChanges();
    await fixture.whenStable();
    bindSpy.and.rejectWith(new Error('network error'));

    const picked: Variable[] = [];
    component.pick.subscribe(v => picked.push(v));

    // A rejection here would otherwise become an unhandled promise rejection - activateLeaf fires
    // pickLeaf without awaiting it, so this only proves the underlying promise settles cleanly.
    component.activateLeaf(leaf);
    await fixture.whenStable();

    expect(picked).toEqual([]);
    expect(component.pickFailed()).toBeTrue();
  });
});

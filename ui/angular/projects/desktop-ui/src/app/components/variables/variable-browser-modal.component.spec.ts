import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import type { Variable, VariableCatalogNode } from '@macro-deck/runtime';
import { VariablesManagerComponent } from './variables-manager.component';
import { VariableBrowserModalComponent } from './variable-browser-modal.component';

function variable(overrides: Partial<Variable>): Variable {
  return {
    id: 'id',
    name: 'name',
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: '',
    ...overrides,
  };
}

function node(overrides: Partial<VariableCatalogNode>): VariableCatalogNode {
  return {
    id: 'resource',
    name: 'resource',
    displayName: 'Resource',
    hasChildren: false,
    ...overrides,
  };
}

const userVar = variable({ id: 'u1', name: 'greeting' });
const numericVar = variable({ id: 'u2', name: 'counter', type: 'numeric' });
const spotifyVar = variable({
  id: 's1', name: 'is_playing', classification: 'integration', ownerIntegrationId: 'spotify', type: 'boolean',
});
const buttonVar = variable({
  id: 'b1', name: 'toggled', scope: 'widget', scopeRefId: 'w1', classification: 'widget', type: 'boolean',
});

describe('VariableBrowserModalComponent', () => {
  let fixture: ComponentFixture<VariableBrowserModalComponent>;
  let component: VariableBrowserModalComponent;
  let discoverCatalogVariablesSpy: jasmine.Spy;

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'getVariableCatalogProviders',
      'discoverCatalogVariables',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.getVariableCatalogProviders.and.resolveTo({
      providers: [
        { integrationId: 'home-assistant', name: 'Home Assistant', supportsManualIds: false, supportsSearch: true },
        { integrationId: 'adb', name: 'ADB', supportsManualIds: false, supportsSearch: true },
      ],
    });
    apiSpy.discoverCatalogVariables.and.resolveTo({ nodes: [], hasMore: false, available: true });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    discoverCatalogVariablesSpy = apiSpy.discoverCatalogVariables;

    TestBed.configureTestingModule({
      imports: [VariableBrowserModalComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(VariableBrowserModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variables', [userVar, numericVar, spotifyVar, buttonVar]);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('lists an integration once whether it provides variables, offers a catalog, or both', () => {
    expect(component.totalCount()).toBe(4);
    expect(component.userCount()).toBe(2);
    expect(component.scopeCount()).toBe(1);
    // `spotify` provides one variable and no catalog; `home-assistant` provides none but has a
    // catalog and still earns a rail entry; `adb` likewise. Each appears exactly once.
    expect(component.integrationSources()).toEqual([
      { integrationId: 'adb', name: 'adb', count: 0 },
      { integrationId: 'home-assistant', name: 'home-assistant', count: 0 },
      { integrationId: 'spotify', name: 'spotify', count: 1 },
    ]);
  });

  it('keeps one entry for an integration that both provides variables and offers a catalog', async () => {
    fixture.componentRef.setInput('variables', [
      userVar,
      variable({ id: 'h1', name: 'sun_state', classification: 'integration', ownerIntegrationId: 'home-assistant' }),
    ]);
    await fixture.whenStable();

    const homeAssistant = component.integrationSources()
      .filter(entry => entry.integrationId === 'home-assistant');
    expect(homeAssistant).toEqual([{ integrationId: 'home-assistant', name: 'home-assistant', count: 1 }]);
  });

  it('offers the scope source in pick mode only when scope-locals exist', async () => {
    fixture.componentRef.setInput('mode', 'pick');
    expect(component.showScopeSource()).toBeTrue();

    fixture.componentRef.setInput('variables', [userVar, spotifyVar]);
    await fixture.whenStable();

    expect(component.showScopeSource()).toBeFalse();
  });

  it('always offers the scope source in manage mode when a scopeRefId is set', async () => {
    fixture.componentRef.setInput('scopeRefId', 'w1');
    fixture.componentRef.setInput('variables', [userVar]);
    await fixture.whenStable();

    expect(component.showScopeSource()).toBeTrue();
  });

  it('hides the scope source in manage mode without a scopeRefId or locals', async () => {
    fixture.componentRef.setInput('variables', [userVar]);
    await fixture.whenStable();

    expect(component.showScopeSource()).toBeFalse();
  });

  it('narrows everything to the accepted types in pick mode', async () => {
    fixture.componentRef.setInput('mode', 'pick');
    fixture.componentRef.setInput('acceptedTypes', ['boolean']);
    await fixture.whenStable();

    expect(component.totalCount()).toBe(2);
    expect(component.userCount()).toBe(0);
    expect(component.scopeCount()).toBe(1);
  });

  it('follows the selected source in the manager title', () => {
    expect(component.managerTitle()).toBe('All variables');

    component.selectUser();
    expect(component.managerTitle()).toBe('User variables');

    component.selectScope();
    expect(component.managerTitle()).toBe('This widget');

    component.selectIntegration('spotify');
    expect(component.managerTitle()).toBe('spotify');
  });

  it('falls back to all when the selected integration stops providing variables and offers no catalog', async () => {
    component.selectIntegration('spotify');
    expect(component.source().kind).toBe('integration');

    fixture.componentRef.setInput('variables', [userVar]);
    await fixture.whenStable();

    expect(component.source()).toEqual({ kind: 'all' });
  });

  it('re-emits a picked variable and closes after the dismiss animation', () => {
    jasmine.clock().install();
    try {
      const picked: Variable[] = [];
      const closed: boolean[] = [];
      component.pick.subscribe(v => picked.push(v));
      component.close.subscribe(() => closed.push(true));

      component.onPick(userVar);
      jasmine.clock().tick(1000);

      expect(picked).toEqual([userVar]);
      expect(closed).toEqual([true]);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('performs zero discovery calls while a source with no catalog is selected', async () => {
    component.selectUser();
    component.selectIntegration('spotify');
    component.selectScope();
    await fixture.whenStable();

    expect(discoverCatalogVariablesSpy).not.toHaveBeenCalled();
  });

  async function settleCatalog(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  it('lists an integration catalog inside the variable list, not beside it', async () => {
    // The catalog used to be a browser of its own next to the list; its entries now belong to the
    // same list, so what proves it arrived is the query going out, not a second component existing.
    component.selectIntegration('home-assistant');
    await settleCatalog();

    expect(fixture.nativeElement.querySelector('shared-variables-manager')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('shared-variable-catalog-tree')).toBeNull();
    expect(discoverCatalogVariablesSpy).toHaveBeenCalled();
  });

  it('re-queries the new catalog when the integration is switched', async () => {
    component.selectIntegration('home-assistant');
    await settleCatalog();
    discoverCatalogVariablesSpy.calls.reset();

    component.selectIntegration('adb');
    await settleCatalog();

    expect(discoverCatalogVariablesSpy).toHaveBeenCalledWith(jasmine.objectContaining({ integrationId: 'adb' }));
    expect(discoverCatalogVariablesSpy).not.toHaveBeenCalledWith(jasmine.objectContaining({ integrationId: 'home-assistant' }));
  });

  it('refuses a leaf the caller cannot use before it is materialized', async () => {
    // Checked before anything is bound: binding first and rejecting the result afterwards leaves a
    // variable the caller then refuses to use behind on every rejected leaf. The narrowing moved
    // into the manager with the rows themselves, so that is where it is asserted.
    fixture.componentRef.setInput('mode', 'pick');
    fixture.componentRef.setInput('acceptedTypes', ['numeric']);
    fixture.componentRef.setInput('writableOnly', true);
    component.selectIntegration('home-assistant');
    await fixture.whenStable();

    const manager = fixture.debugElement.query(By.directive(VariablesManagerComponent))
      .componentInstance as VariablesManagerComponent;

    expect(manager.isCatalogBindable(node({ id: 'n1', type: 'numeric', canWrite: true }))).toBeTrue();
    expect(manager.isCatalogBindable(node({ id: 'n2', type: 'numeric' }))).toBeFalse();
    expect(manager.isCatalogBindable(node({ id: 'b1', type: 'boolean', canWrite: true }))).toBeFalse();
  });

  it('renders one rail entry per source', () => {
    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('shared-rail-item'),
    ).map(el => (el as HTMLElement).textContent?.trim() ?? '');

    expect(labels.some(l => l.startsWith('All variables'))).toBeTrue();
    expect(labels.some(l => l.startsWith('User'))).toBeTrue();
    expect(labels.some(l => l.startsWith('This widget'))).toBeTrue();
    expect(labels.some(l => l.startsWith('spotify'))).toBeTrue();
  });
});

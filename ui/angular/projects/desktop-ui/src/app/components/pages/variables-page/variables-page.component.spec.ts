import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import type { VariableCatalogProvider, IpcIntegration, Variable, VariableClassification } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { VariablesPageComponent } from './variables-page.component';

function variable(
  id: string,
  classification: VariableClassification,
  ownerIntegrationId?: string,
): Variable {
  return {
    id,
    name: `var_${id}`,
    scope: 'global',
    type: 'text',
    classification,
    ownerIntegrationId,
    value: '',
  };
}

function ipcIntegration(id: string, name: string, variableCount: number): IpcIntegration {
  return {
    id,
    name,
    version: '1.0.0',
    isInternal: true,
    enabled: true,
    actionCount: 0,
    variableCount,
    supportsConfigFlow: false,
    allowsMultipleConfigurations: false,
    configuredEntryCount: 0,
    hasIcon: false,
    issueCount: 0,
  };
}

describe('VariablesPageComponent', () => {
  let fixture: ComponentFixture<VariablesPageComponent>;
  let component: VariablesPageComponent;
  let queryParams: BehaviorSubject<ParamMap>;
  let routeStub: { queryParamMap: BehaviorSubject<ParamMap>; snapshot: { queryParamMap: ParamMap } };
  let routerSpy: jasmine.SpyObj<Router>;

  const variables: Variable[] = [
    variable('1', 'user'),
    variable('2', 'integration', 'spotify'),
    variable('3', 'integration', 'spotify'),
    variable('4', 'integration', 'obs'),
  ];

  function setQueryParams(params: Record<string, string>): void {
    const map = convertToParamMap(params);
    routeStub.snapshot.queryParamMap = map;
    queryParams.next(map);
  }

  // `obs` both provides a variable and offers a catalog - the shape that used to produce two rail
  // entries for one integration. `home-assistant` offers only a catalog.
  const catalogProviders: VariableCatalogProvider[] = [
    { integrationId: 'home-assistant', name: 'Home Assistant', supportsManualIds: true, supportsSearch: true },
    { integrationId: 'obs', name: 'OBS Studio', supportsManualIds: false, supportsSearch: true },
  ];

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'getVariableCatalogProviders',
      'discoverCatalogVariables',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables });
    apiSpy.getIntegrations.and.resolveTo({
      integrations: [
        ipcIntegration('spotify', 'Spotify', 2),
        ipcIntegration('obs', 'OBS Studio', 1),
      ],
    });
    apiSpy.getVariableCatalogProviders.and.resolveTo({ providers: catalogProviders });
    apiSpy.discoverCatalogVariables.and.resolveTo({ nodes: [], hasMore: false, available: true });
    apiSpy.onNotification.and.callFake(() => new Subject());

    queryParams = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    routeStub = { queryParamMap: queryParams, snapshot: { queryParamMap: queryParams.value } };
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    TestBed.configureTestingModule({
      imports: [VariablesPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ActivatedRoute, useValue: routeStub },
        { provide: Router, useValue: routerSpy },
      ],
    });

    fixture = TestBed.createComponent(VariablesPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    // A second round-trip: variable catalog providers load through a separate signal chain
    // (VariableCatalogService.loadProviders(), triggered independently of the initial render) and
    // need one more microtask turn to resolve and reach the DOM.
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('counts variables per source', () => {
    expect(component.totalCount()).toBe(4);
    expect(component.userCount()).toBe(1);
  });

  it('derives one rail entry per integration - providing, cataloging, or both - sorted by name', () => {
    expect(component.integrationSources()).toEqual([
      { integrationId: 'home-assistant', name: 'home-assistant', count: 0 },
      { integrationId: 'obs', name: 'OBS Studio', count: 1 },
      { integrationId: 'spotify', name: 'Spotify', count: 2 },
    ]);
  });

  it('lists an integration that both provides variables and offers a catalog exactly once', () => {
    const obsEntries = component.integrationSources().filter(entry => entry.integrationId === 'obs');
    const obsRailItems = Array.from(fixture.nativeElement.querySelectorAll('shared-rail-item') as NodeListOf<Element>)
      .filter(el => el.textContent?.includes('OBS Studio'));

    expect(obsEntries.length).toBe(1);
    expect(obsRailItems.length).toBe(1);
  });

  it('starts with all variables selected and no filters applied', () => {
    expect(component.isAllSelected()).toBeTrue();
    expect(component.source()).toEqual({ kind: 'all' });
    expect(component.managerTitle()).toBe('All variables');
  });

  it('selects the integration source from the query param', () => {
    setQueryParams({ integrationId: 'spotify' });

    expect(component.isIntegrationSelected('spotify')).toBeTrue();
    expect(component.source()).toEqual({ kind: 'integration', integrationId: 'spotify' });
    expect(component.managerTitle()).toBe('Spotify');
  });

  it('falls back to the integration id when the integration list does not know it', () => {
    setQueryParams({ integrationId: 'unknown' });

    expect(component.managerTitle()).toBe('unknown');
  });

  it('resets to all variables when the query param disappears', () => {
    setQueryParams({ integrationId: 'spotify' });
    setQueryParams({});

    expect(component.isAllSelected()).toBeTrue();
    expect(component.source()).toEqual({ kind: 'all' });
  });

  it('routes integration selection through the query param', () => {
    component.selectIntegration('obs');

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/variables'], {
      queryParams: { integrationId: 'obs' },
    });
  });

  it('applies the user source without touching the router when no param is set', () => {
    component.selectUser();

    expect(component.isUserSelected()).toBeTrue();
    expect(component.source()).toEqual({ kind: 'user' });
    expect(component.managerTitle()).toBe('User variables');
    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });

  it('clears an active integration param when switching to the user source', () => {
    setQueryParams({ integrationId: 'spotify' });

    component.selectUser();

    expect(component.isUserSelected()).toBeTrue();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/variables']);
  });

  it('keeps the user selection when the param-clearing navigation completes', () => {
    setQueryParams({ integrationId: 'spotify' });
    component.selectUser();

    setQueryParams({});

    expect(component.isUserSelected()).toBeTrue();
  });

  function railCreateButton(): HTMLElement | null {
    return fixture.nativeElement.querySelector('.rail-header shared-button');
  }

  it('shows the create button in the rail header and hides it inside the manager', () => {
    expect(railCreateButton()).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.vars-header shared-button')).toBeNull();
  });

  it('renders the rail create button icon-only with an accessible label, like the other rail pages', () => {
    const button = railCreateButton();

    expect(button?.getAttribute('aria-label')).toBe('New variable');
    expect(button?.querySelector('.icon-plus')).toBeTruthy();
    expect(button?.textContent?.trim()).toBe('');
  });

  it('hides the rail create button while an integration source is selected', async () => {
    setQueryParams({ integrationId: 'spotify' });
    await fixture.whenStable();

    expect(railCreateButton()).toBeNull();
  });

  it('opens the manager create modal from the rail create button', async () => {
    railCreateButton()?.querySelector('button')?.click();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('shared-modal')).toBeTruthy();
  });

  it('makes the manager host a bounded flex column so its list scrolls (regression for #109)', () => {
    document.body.appendChild(fixture.nativeElement);
    try {
      const host = fixture.nativeElement.querySelector('shared-variables-manager') as HTMLElement;
      expect(host).toBeTruthy();

      const styles = getComputedStyle(host);
      expect(styles.display).toBe('flex');
      expect(styles.flexDirection).toBe('column');
      expect(styles.minHeight).toBe('0px');
      expect(styles.flexGrow).toBe('1');
    } finally {
      document.body.removeChild(fixture.nativeElement);
    }
  });

  it('gives a catalog-only integration a rail entry under the same Integrations section', () => {
    expect(component.catalogProviders()).toEqual(catalogProviders);

    const sections = Array.from(fixture.nativeElement.querySelectorAll('.rail-section') as NodeListOf<Element>)
      .map(el => el.textContent?.trim());
    expect(sections).toEqual(['Integrations']);

    const catalogItem = Array.from(fixture.nativeElement.querySelectorAll('shared-rail-item') as NodeListOf<Element>)
      .find(el => el.textContent?.includes('home-assistant'));
    expect(catalogItem).toBeTruthy();
  });

  it('lists an integration catalog inside the variable list, not in a browser of its own', async () => {
    // The catalog used to sit in a separate section with its own scroller below the list, which read
    // as a second, unrelated place rather than as more of the same integration's variables.
    setQueryParams({ integrationId: 'home-assistant' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.catalogIntegrationId()).toBe('home-assistant');
    expect(fixture.nativeElement.querySelector('shared-variables-manager')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-variable-catalog-tree'))
      .withContext('the catalog belongs in the manager\'s own list')
      .toBeNull();
  });

  it('lists no catalog for an integration that offers none', async () => {
    setQueryParams({ integrationId: 'spotify' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.catalogIntegrationId()).toBeNull();
  });
});

import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import type { ActionDefinition, IpcIntegration } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { DeveloperModeService } from '../../../services/developer-mode.service';
import { DeveloperPageComponent } from './developer-page.component';

function ipcIntegration(id: string, name: string, actionCount: number): IpcIntegration {
  return {
    id,
    name,
    version: '1.0.0',
    isInternal: true,
    enabled: true,
    actionCount,
    variableCount: 0,
    supportsConfigFlow: false,
    allowsMultipleConfigurations: false,
    configuredEntryCount: 0,
    hasIcon: false,
    issueCount: 0,
  };
}

function actionDef(id: string, integrationId: string, integrationName: string, name: string): ActionDefinition {
  return { id, integrationId, integrationName, name, description: '', parameters: [] };
}

describe('DeveloperPageComponent', () => {
  let fixture: ComponentFixture<DeveloperPageComponent>;
  let component: DeveloperPageComponent;
  let queryParams: BehaviorSubject<ParamMap>;
  let routerSpy: jasmine.SpyObj<Router>;

  function setQueryParams(params: Record<string, string>): void {
    queryParams.next(convertToParamMap(params));
  }

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getActions',
      'getIntegrations',
      'getEventDefinitions',
      'getVariables',
      'renderTemplate',
      'triggerEvent',
      'onNotification',
      'getPluginCompatibility',
      'getPluginRuntime',
      'getPluginTokens',
      'getPluginSessions',
      'getPluginPairingRequests',
      'getPairedPlugins',
      'getStoreStatus',
    ]);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: [] });
    apiSpy.getPluginRuntime.and.resolveTo({ plugins: [] });
    apiSpy.getPluginTokens.and.resolveTo({ tokens: [] });
    apiSpy.getPluginSessions.and.resolveTo({ sessions: [] });
    apiSpy.getPluginPairingRequests.and.resolveTo({ requests: [] });
    apiSpy.getPairedPlugins.and.resolveTo({ registrations: [] });
    apiSpy.getStoreStatus.and.resolveTo({
      developerMode: false,
      registry: { hasCatalog: false, sequence: 0, refreshing: false, stale: false },
    });
    apiSpy.getEventDefinitions.and.resolveTo({ events: [] });
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({
      integrations: [ipcIntegration('system', 'System', 2), ipcIntegration('obs', 'OBS Studio', 1)],
    });
    apiSpy.getActions.and.resolveTo({
      actions: [
        actionDef('set-volume', 'system', 'System', 'Set Volume'),
        actionDef('mute', 'system', 'System', 'Mute'),
        actionDef('toggle-source', 'obs', 'OBS Studio', 'Toggle Source'),
      ],
    });

    queryParams = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [DeveloperPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        // The unified tab renders its rail only with Developer Mode on; these cases are about tab
        // routing, not the gate, which the tab's own spec covers.
        { provide: DeveloperModeService, useValue: { enabled: signal(true), ensureLoaded: () => Promise.resolve() } },
        { provide: ActivatedRoute, useValue: { queryParamMap: queryParams } },
        { provide: Router, useValue: routerSpy },
      ],
    });

    fixture = TestBed.createComponent(DeveloperPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('counts every loaded action', () => {
    expect(component.totalCount()).toBe(3);
  });

  it('starts on the run-action tab with the runner visible', () => {
    expect(component.activeTab()).toBe('run-action');
    expect(fixture.nativeElement.querySelector('shared-tab-bar')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-action-runner')).toBeTruthy();
  });

  it('derives one rail entry per integration that provides actions, sorted by name', () => {
    expect(component.integrationSources()).toEqual([
      { integrationId: 'obs', name: 'OBS Studio', count: 1 },
      { integrationId: 'system', name: 'System', count: 2 },
    ]);
  });

  it('starts on all actions', () => {
    expect(component.isAllSelected()).toBeTrue();
    expect(component.runnerTitle()).toBe('All actions');
  });

  it('selects the integration source from the query param', () => {
    setQueryParams({ integrationId: 'system' });

    expect(component.isIntegrationSelected('system')).toBeTrue();
    expect(component.runnerTitle()).toBe('System');
  });

  it('falls back to the integration id when nothing provides a name', () => {
    setQueryParams({ integrationId: 'unknown' });

    expect(component.runnerTitle()).toBe('unknown');
  });

  it('resets to all actions when the query param disappears', () => {
    setQueryParams({ integrationId: 'system' });
    setQueryParams({});

    expect(component.isAllSelected()).toBeTrue();
  });

  it('routes source selection through the query param, merging so the tab survives', () => {
    component.selectIntegration('obs');
    expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
      queryParams: { integrationId: 'obs' },
      queryParamsHandling: 'merge',
    }));

    component.selectAll();
    expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
      queryParams: { integrationId: null, actionId: null },
      queryParamsHandling: 'merge',
    }));
  });

  it('takes the active tab from the query param, falling back for an unknown one', () => {
    setQueryParams({ tab: 'logs' });
    expect(component.activeTab()).toBe('logs');

    setQueryParams({ tab: 'nonsense' });
    expect(component.activeTab()).toBe('run-action');

    setQueryParams({});
    expect(component.activeTab()).toBe('run-action');
  });

  it('offers the template scratchpad, the event trigger, the log viewer, the UI previews, plugin development and the restart action as tabs', () => {
    expect(component.tabs().map(t => t.id)).toEqual([
      'run-action', 'template', 'events', 'logs', 'previews', 'plugin-development', 'maintenance',
    ]);
  });

  it('has exactly one plugin tab, rather than the three it was merged from', () => {
    const pluginTabs = component.tabs().filter(t => /plugin|compatib/i.test(t.id));
    expect(pluginTabs.map(t => t.id)).toEqual(['plugin-development']);
  });

  it('lands a bookmark of any of the three former tabs on the unified tab, in the section it meant', async () => {
    const expectedSection: Record<string, string> = {
      'plugin-tokens': 'Credentials',
      'managed-plugins': 'Managed plugins',
      compatibility: 'Compatibility',
    };

    for (const [legacy, section] of Object.entries(expectedSection)) {
      setQueryParams({ tab: legacy });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.activeTab()).toBe('plugin-development');
      expect(fixture.nativeElement.querySelector('app-plugin-development-tab')).toBeTruthy();
      const active = fixture.nativeElement.querySelector('app-plugin-development-tab .rail-item.active');
      expect((active as HTMLElement).textContent).toContain(section);
    }
  });

  it('renders the template and events tabs when the query param selects them', async () => {
    setQueryParams({ tab: 'template' });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('app-template-tab')).toBeTruthy();

    setQueryParams({ tab: 'events' });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('app-events-tab')).toBeTruthy();
  });

  it('writes the tab back to the URL, and drops the param for the default tab', () => {
    component.selectTab('logs');
    expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
      queryParams: { tab: 'logs' },
      queryParamsHandling: 'merge',
    }));

    component.selectTab('run-action');
    expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
      queryParams: { tab: null },
      queryParamsHandling: 'merge',
    }));
  });
});

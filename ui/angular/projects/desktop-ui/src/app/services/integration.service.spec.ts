import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import type { GetIntegrationsResponse, IntegrationIssuesChangedEvent, IntegrationsChangedEvent, IpcIntegration } from '@macro-deck/runtime';
import { IntegrationService } from './integration.service';

describe('IntegrationService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let issuesChanged: Subject<IntegrationIssuesChangedEvent>;
  let service: IntegrationService;

  function ipcIntegration(overrides: Partial<IpcIntegration> = {}): IpcIntegration {
    return {
      id: 'app.macro-deck.spotify',
      name: 'Spotify',
      version: '1.0.0',
      isInternal: true,
      enabled: true,
      actionCount: 0,
      variableCount: 0,
      supportsConfigFlow: true,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 1,
      hasIcon: false,
      issueCount: 0,
      issueSeverity: null,
      isInitialized: true,
      variablesDependOnConfiguration: false,
      ...overrides,
    };
  }

  beforeEach(() => {
    issuesChanged = new Subject<IntegrationIssuesChangedEvent>();

    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getIntegrations']);
    apiSpy.onNotification.and.callFake((name: string) => {
      switch (name) {
        case 'IntegrationIssuesChangedEvent':
          return issuesChanged.asObservable() as Observable<never>;
        default:
          return new Subject<never>().asObservable();
      }
    });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(IntegrationService);
  });

  it('patches issueCount and issueSeverity for the matching integration', () => {
    service.integrations.set([
      ipcIntegrationAsIntegration(ipcIntegration({ id: 'app.macro-deck.spotify' })),
      ipcIntegrationAsIntegration(ipcIntegration({ id: 'app.macro-deck.obs', name: 'OBS' })),
    ]);

    issuesChanged.next({
      integrationId: 'app.macro-deck.spotify',
      issues: [{ id: 'i1', title: 'Disconnected', severity: 'error', actionLabel: 'Reconnect' }],
      issueCount: 1,
      severity: 'error',
    });

    const spotify = service.integrations().find(i => i.id === 'app.macro-deck.spotify');
    expect(spotify?.issueCount).toBe(1);
    expect(spotify?.issueSeverity).toBe('error');
  });

  it('leaves every other integration untouched', () => {
    service.integrations.set([
      ipcIntegrationAsIntegration(ipcIntegration({ id: 'app.macro-deck.spotify' })),
      ipcIntegrationAsIntegration(ipcIntegration({ id: 'app.macro-deck.obs', name: 'OBS', issueCount: 2 })),
    ]);

    issuesChanged.next({
      integrationId: 'app.macro-deck.spotify',
      issues: [],
      issueCount: 0,
      severity: null,
    });

    const obs = service.integrations().find(i => i.id === 'app.macro-deck.obs');
    expect(obs?.issueCount).toBe(2);
  });

  it('ignores an event for an integration id that is not in the list', () => {
    const before = [ipcIntegrationAsIntegration(ipcIntegration({ id: 'app.macro-deck.spotify' }))];
    service.integrations.set(before);

    issuesChanged.next({
      integrationId: 'app.macro-deck.unknown',
      issues: [{ id: 'i1', title: 'Disconnected', severity: 'error', actionLabel: 'Reconnect' }],
      issueCount: 1,
      severity: 'error',
    });

    expect(service.integrations()).toEqual(before);
  });

  it('defaults isInitialized and variablesDependOnConfiguration to false when the DTO omits them', async () => {
    const dto = ipcIntegration();
    delete (dto as Partial<IpcIntegration>).isInitialized;
    delete (dto as Partial<IpcIntegration>).variablesDependOnConfiguration;
    apiSpy.getIntegrations.and.resolveTo({ integrations: [dto] });

    await service.loadIntegrations();

    const integration = service.integrations()[0];
    expect(integration.isInitialized).toBeFalse();
    expect(integration.variablesDependOnConfiguration).toBeFalse();
  });

  it('carries isInitialized and variablesDependOnConfiguration through when the DTO sets them', async () => {
    apiSpy.getIntegrations.and.resolveTo({
      integrations: [ipcIntegration({ isInitialized: true, variablesDependOnConfiguration: true })],
    });

    await service.loadIntegrations();

    const integration = service.integrations()[0];
    expect(integration.isInitialized).toBeTrue();
    expect(integration.variablesDependOnConfiguration).toBeTrue();
  });

  // Issue #754: without the version from the host the UI cannot tell a replaced icon from the old one.
  it('carries the icon version through so a replaced icon can be told apart', async () => {
    apiSpy.getIntegrations.and.resolveTo({
      integrations: [ipcIntegration({ hasIcon: true, iconVersion: 'abc123' })],
    });

    await service.loadIntegrations();

    expect(service.integrations()[0].iconVersion).toBe('abc123');
  });

  it('defaults providedCapabilities to an empty array when the DTO omits it', async () => {
    const dto = ipcIntegration();
    delete (dto as Partial<IpcIntegration>).providedCapabilities;
    apiSpy.getIntegrations.and.resolveTo({ integrations: [dto] });

    await service.loadIntegrations();

    // A host older than the UI must not blank the whole page with `undefined.map(...)`.
    expect(service.integrations()[0].providedCapabilities).toEqual([]);
  });
});

function ipcIntegrationAsIntegration(dto: IpcIntegration) {
  return {
    id: dto.id,
    // Mirrors the service: the host's name is a LocalizedText, and the fixtures above give literals.
    name: resolveLocalizedText(dto.name, { translate: (scope, key) => `[[${scope}:${key}]]` }),
    version: dto.version,
    isInternal: dto.isInternal,
    enabled: dto.enabled,
    actionCount: dto.actionCount,
    variableCount: dto.variableCount,
    supportsConfigFlow: dto.supportsConfigFlow,
    allowsMultipleConfigurations: dto.allowsMultipleConfigurations,
    configuredEntryCount: dto.configuredEntryCount,
    hasIcon: dto.hasIcon,
    iconVersion: dto.iconVersion ?? null,
    issueCount: dto.issueCount,
    issueSeverity: dto.issueSeverity ?? null,
    isInitialized: dto.isInitialized ?? false,
    variablesDependOnConfiguration: dto.variablesDependOnConfiguration ?? false,
    providedCapabilities: dto.providedCapabilities ?? [],
  };
}

describe('IntegrationService catalogue refresh', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let issuesChanged: Subject<IntegrationIssuesChangedEvent>;
  let integrationsChanged: Subject<IntegrationsChangedEvent>;
  let service: IntegrationService;

  const settle = () => new Promise(resolve => setTimeout(resolve));

  function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (error: unknown) => void;
    const promise = new Promise<T>((res, rej) => {
      resolve = res;
      reject = rej;
    });
    return { promise, resolve, reject };
  }

  function ipc(overrides: Partial<IpcIntegration> = {}): IpcIntegration {
    return {
      id: 'app.macro-deck.spotify',
      name: 'Spotify',
      version: '1.0.0',
      isInternal: true,
      enabled: true,
      actionCount: 0,
      variableCount: 0,
      supportsConfigFlow: false,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 0,
      hasIcon: false,
      issueCount: 0,
      issueSeverity: null,
      isInitialized: true,
      variablesDependOnConfiguration: false,
      ...overrides,
    };
  }

  const response = (...integrations: IpcIntegration[]): GetIntegrationsResponse =>
    ({ integrations }) as GetIntegrationsResponse;

  beforeEach(() => {
    issuesChanged = new Subject<IntegrationIssuesChangedEvent>();
    integrationsChanged = new Subject<IntegrationsChangedEvent>();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getIntegrations']);
    apiSpy.onNotification.and.callFake((name: string) => {
      switch (name) {
        case 'IntegrationIssuesChangedEvent':
          return issuesChanged.asObservable() as Observable<never>;
        case 'IntegrationsChangedEvent':
          return integrationsChanged.asObservable() as Observable<never>;
        default:
          return new Subject<never>().asObservable();
      }
    });
    spyOn(console, 'error');

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(IntegrationService);
  });

  it('shows a plugin installed after the list was loaded', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc()));
    await service.loadIntegrations();

    apiSpy.getIntegrations.and.resolveTo(response(ipc(), ipc({ id: 'com.example.repro', name: 'Repro', isInternal: false })));
    integrationsChanged.next({ integrationId: 'com.example.repro' });
    await settle();

    expect(service.integrations().map(i => i.id)).toEqual(['app.macro-deck.spotify', 'com.example.repro']);
  });

  it('shows the new version and variable count after a plugin upgrade', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc({ id: 'com.example.repro', version: '1.0.0', variableCount: 1 })));
    await service.loadIntegrations();

    apiSpy.getIntegrations.and.resolveTo(response(ipc({ id: 'com.example.repro', version: '1.0.1', variableCount: 2 })));
    integrationsChanged.next({ integrationId: 'com.example.repro' });
    await settle();

    const repro = service.integrations().find(i => i.id === 'com.example.repro');
    expect(repro?.version).toBe('1.0.1');
    expect(repro?.variableCount).toBe(2);
  });

  it('refreshes without showing the loading state', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc()));
    await service.loadIntegrations();
    const pending = deferred<GetIntegrationsResponse>();
    apiSpy.getIntegrations.and.returnValue(pending.promise);

    integrationsChanged.next({ integrationId: 'app.macro-deck.spotify' });
    await settle();

    expect(service.isLoading()).toBeFalse();
    pending.resolve(response(ipc()));
    await settle();
  });

  it('keeps the last good list when a refresh fails', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc()));
    await service.loadIntegrations();

    apiSpy.getIntegrations.and.rejectWith(new Error('host restarting'));
    integrationsChanged.next({ integrationId: 'app.macro-deck.spotify' });
    await settle();

    expect(service.integrations().map(i => i.id)).toEqual(['app.macro-deck.spotify']);
    expect(service.loadError()).toBeNull();
  });

  it('applies the newer response when an older one resolves last', async () => {
    const older = deferred<GetIntegrationsResponse>();
    const newer = deferred<GetIntegrationsResponse>();
    apiSpy.getIntegrations.and.returnValues(older.promise, newer.promise);

    const explicitLoad = service.loadIntegrations();
    integrationsChanged.next({ integrationId: 'com.example.repro' });
    newer.resolve(response(ipc({ id: 'com.example.repro', version: '1.0.1' })));
    await settle();
    older.resolve(response(ipc({ id: 'com.example.repro', version: '1.0.0' })));
    await explicitLoad;

    expect(service.integrations().map(i => i.version)).toEqual(['1.0.1']);
    expect(service.isLoading()).toBeFalse();
    expect(service.loadError()).toBeNull();
  });

  it('keeps an issue update that arrives while a refresh is in flight', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc()));
    await service.loadIntegrations();
    const pending = deferred<GetIntegrationsResponse>();
    apiSpy.getIntegrations.and.returnValue(pending.promise);

    integrationsChanged.next({ integrationId: 'app.macro-deck.spotify' });
    issuesChanged.next({ integrationId: 'app.macro-deck.spotify', issues: [], issueCount: 2, severity: 'warning' });
    pending.resolve(response(ipc({ issueCount: 0, issueSeverity: null })));
    await settle();

    const spotify = service.integrations().find(i => i.id === 'app.macro-deck.spotify');
    expect(spotify?.issueCount).toBe(2);
    expect(spotify?.issueSeverity).toBe('warning');
  });

  it('answers a burst of changes with one follow-up request that ends on the latest list', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc()));
    await service.loadIntegrations();
    apiSpy.getIntegrations.calls.reset();
    const first = deferred<GetIntegrationsResponse>();
    apiSpy.getIntegrations.and.returnValues(first.promise, Promise.resolve(response(ipc({ version: '2.0.0' }))));

    for (let i = 0; i < 5; i++) {
      integrationsChanged.next({ integrationId: 'app.macro-deck.spotify' });
    }
    first.resolve(response(ipc({ version: '1.5.0' })));
    await settle();
    await settle();

    expect(apiSpy.getIntegrations).toHaveBeenCalledTimes(2);
    expect(service.integrations().map(i => i.version)).toEqual(['2.0.0']);
  });

  it('does not re-apply an issue update from a failed refresh on a later one', async () => {
    apiSpy.getIntegrations.and.resolveTo(response(ipc()));
    await service.loadIntegrations();
    const failing = deferred<GetIntegrationsResponse>();
    apiSpy.getIntegrations.and.returnValue(failing.promise);

    integrationsChanged.next({ integrationId: 'app.macro-deck.spotify' });
    issuesChanged.next({ integrationId: 'app.macro-deck.spotify', issues: [], issueCount: 3, severity: 'error' });
    failing.reject(new Error('host restarting'));
    await settle();

    apiSpy.getIntegrations.and.resolveTo(response(ipc({ issueCount: 0, issueSeverity: null })));
    integrationsChanged.next({ integrationId: 'app.macro-deck.spotify' });
    await settle();

    expect(service.integrations().find(i => i.id === 'app.macro-deck.spotify')?.issueCount).toBe(0);
  });
});

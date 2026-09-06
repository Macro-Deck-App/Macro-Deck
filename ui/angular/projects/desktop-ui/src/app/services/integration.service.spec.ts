import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import type { IntegrationIssuesChangedEvent, IpcIntegration } from '@macro-deck/runtime';
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

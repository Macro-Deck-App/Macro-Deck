import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { IpcIntegration, PluginCompatibilityReport, PluginInstallActionResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { IntegrationService } from '../../../services/integration.service';
import { FileOpenService } from '../../../services/file-open.service';
import { IntegrationFilterService } from '../../../services/integration-filter.service';
import { IntegrationsPageComponent } from './integrations-page.component';

interface ToggleAccess {
  confirmDisable(): Promise<void>;
  cancelDisable(): void;
  disableTarget(): unknown;
  configuringId(): string | null;
}

describe('IntegrationsPageComponent enable toggle', () => {
  let fixture: ComponentFixture<IntegrationsPageComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;

  function integrationDto(overrides: Partial<IpcIntegration>): IpcIntegration {
    return {
      id: 'app.test.integration',
      name: 'Test Integration',
      version: '1.0.0',
      isInternal: true,
      enabled: false,
      actionCount: 0,
      variableCount: 0,
      supportsConfigFlow: false,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 0,
      hasIcon: false,
      issueCount: 0,
      issueSeverity: null,
      ...overrides,
    } as IpcIntegration;
  }

  async function setup(integrations: IpcIntegration[], routerSpy?: jasmine.SpyObj<Router>): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getIntegrations',
      'setIntegrationEnabled',
      'getIntegrationIconUrl',
      'startConfigFlow',
      'onNotification',
      'getPluginCompatibility',
      'getInstalledPlugins',
    ]);
    apiSpy.getInstalledPlugins.and.resolveTo({ plugins: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations });
    apiSpy.setIntegrationEnabled.and.resolveTo({ success: true });
    apiSpy.startConfigFlow.and.resolveTo({ supported: false });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    // PluginCompatibilityService (issue #418) is a real, root-provided injectable the page pulls in
    // for the compatibility badge - its constructor effect reads this signal.
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: [] });

    TestBed.configureTestingModule({
      imports: [IntegrationsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        routerSpy ? { provide: Router, useValue: routerSpy } : provideRouter([]),
        IntegrationService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    fixture = TestBed.createComponent(IntegrationsPageComponent);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  function access(): ToggleAccess {
    return fixture.componentInstance as unknown as ToggleAccess;
  }

  function toggleInput(): HTMLInputElement {
    return fixture.nativeElement.querySelector('shared-toggle-switch .ts-input');
  }

  function toggleLabel(): HTMLElement {
    return fixture.nativeElement.querySelector('shared-toggle-switch .ts');
  }

  it('keeps the switch checked and asks for confirmation when disabling', async () => {
    await setup([integrationDto({ enabled: true })]);

    toggleInput().click();
    await fixture.whenStable();

    expect(access().disableTarget()).toBeTruthy();
    expect(toggleLabel().classList).toContain('ts-checked');
  });

  it('unchecks the switch once the disable is confirmed', async () => {
    await setup([integrationDto({ enabled: true })]);

    toggleInput().click();
    await fixture.whenStable();
    await access().confirmDisable();
    await fixture.whenStable();

    expect(apiSpy.setIntegrationEnabled).toHaveBeenCalledWith({ id: 'app.test.integration', enabled: false });
    expect(toggleLabel().classList).not.toContain('ts-checked');
    expect(toggleInput().checked).toBeFalse();
  });

  it('keeps the switch checked when the disable is cancelled', async () => {
    await setup([integrationDto({ enabled: true })]);

    toggleInput().click();
    await fixture.whenStable();
    access().cancelDisable();
    await fixture.whenStable();

    expect(apiSpy.setIntegrationEnabled).not.toHaveBeenCalled();
    expect(toggleLabel().classList).toContain('ts-checked');
  });

  it('reverts the switch and opens setup when enabling an unconfigured integration', async () => {
    await setup([integrationDto({ supportsConfigFlow: true })]);

    toggleInput().click();
    await fixture.whenStable();

    expect(access().configuringId()).toBe('app.test.integration');
    expect(apiSpy.setIntegrationEnabled).not.toHaveBeenCalled();
    expect(toggleLabel().classList).not.toContain('ts-checked');
  });

  it('enables a configured integration directly through the host', async () => {
    await setup([integrationDto({})]);

    toggleInput().click();
    await fixture.whenStable();

    expect(apiSpy.setIntegrationEnabled).toHaveBeenCalledWith({ id: 'app.test.integration', enabled: true });
    expect(toggleLabel().classList).toContain('ts-checked');
  });
});

describe('IntegrationsPageComponent declared counts', () => {
  let fixture: ComponentFixture<IntegrationsPageComponent>;

  function integrationDto(overrides: Partial<IpcIntegration>): IpcIntegration {
    return {
      id: 'app.test.integration',
      name: 'Test Integration',
      version: '1.0.0',
      isInternal: true,
      enabled: false,
      actionCount: 3,
      variableCount: 2,
      supportsConfigFlow: false,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 0,
      hasIcon: false,
      issueCount: 0,
      issueSeverity: null,
      ...overrides,
    } as IpcIntegration;
  }

  async function setup(integrations: IpcIntegration[], routerSpy?: jasmine.SpyObj<Router>): Promise<void> {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getIntegrations',
      'setIntegrationEnabled',
      'getIntegrationIconUrl',
      'startConfigFlow',
      'onNotification',
      'getPluginCompatibility',
      'getInstalledPlugins',
    ]);
    apiSpy.getInstalledPlugins.and.resolveTo({ plugins: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: [] });

    TestBed.configureTestingModule({
      imports: [IntegrationsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        routerSpy ? { provide: Router, useValue: routerSpy } : provideRouter([]),
        IntegrationService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    fixture = TestBed.createComponent(IntegrationsPageComponent);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  function metaLinks(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.meta-item-link'));
  }

  it('renders the declared action/variable counts for a disabled integration', async () => {
    await setup([integrationDto({ enabled: false })]);

    const links = metaLinks();
    expect(links[0].textContent).toContain('3');
    expect(links[1].textContent).toContain('2');
  });

  it('routes the actions count link to the detail page\'s Actions tab', async () => {
    const routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);
    await setup([integrationDto({})], routerSpy);

    metaLinks()[0].click();
    await fixture.whenStable();

    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/integrations', 'app.test.integration'],
      { queryParams: { tab: 'actions' } },
    );
  });

  it('routes the variables count link to the detail page\'s Variables tab', async () => {
    const routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);
    await setup([integrationDto({})], routerSpy);

    metaLinks()[1].click();
    await fixture.whenStable();

    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/integrations', 'app.test.integration'],
      { queryParams: { tab: 'variables' } },
    );
  });

  it('shows provider capabilities alongside the existing counts without duplicating them', async () => {
    await setup([integrationDto({
      providedCapabilities: [
        { kind: 'actions', name: 'Actions' },
        { kind: 'variables', name: 'Variables' },
        { kind: 'music-player', name: 'Music Player' },
        { kind: 'weather', name: 'Weather' },
      ],
    })]);

    const chipText = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.meta-item'))
      .map(chip => chip.textContent?.replace(/\s+/g, ' ').trim() ?? '');
    expect(chipText).toContain('3 Actions');
    expect(chipText).toContain('2 Variables');
    expect(chipText).toContain('Music Player');
    expect(chipText).toContain('Weather');
    // The actions/variables kinds must not also render as their own extra chip.
    expect(chipText.filter(text => text.includes('Actions')).length).toBe(1);
    expect(chipText.filter(text => text.includes('Variables')).length).toBe(1);
  });

  it('renders an unrecognised capability kind by its own name', async () => {
    await setup([integrationDto({
      providedCapabilities: [{ kind: 'scene-switcher', name: 'Scene Switcher' }],
    })]);

    const cardText = fixture.nativeElement.querySelector('.integration-card').textContent as string;
    expect(cardText).toContain('Scene Switcher');
  });
});

describe('IntegrationsPageComponent filters', () => {
  let fixture: ComponentFixture<IntegrationsPageComponent>;
  let filters: IntegrationFilterService;

  const STORAGE_KEY = 'md.integrations.filters';

  function integrationDto(overrides: Partial<IpcIntegration>): IpcIntegration {
    return {
      id: 'app.test.integration',
      name: 'Test Integration',
      version: '1.0.0',
      isInternal: true,
      enabled: false,
      actionCount: 0,
      variableCount: 0,
      supportsConfigFlow: false,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 0,
      hasIcon: false,
      issueCount: 0,
      issueSeverity: null,
      providedCapabilities: [],
      ...overrides,
    } as IpcIntegration;
  }

  async function setup(
    integrations: IpcIntegration[],
    compatibility: PluginCompatibilityReport[] = [],
  ): Promise<void> {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getIntegrations',
      'setIntegrationEnabled',
      'getIntegrationIconUrl',
      'startConfigFlow',
      'onNotification',
      'getPluginCompatibility',
      'getInstalledPlugins',
    ]);
    apiSpy.getInstalledPlugins.and.resolveTo({ plugins: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: compatibility });

    TestBed.configureTestingModule({
      imports: [IntegrationsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        IntegrationService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    fixture = TestBed.createComponent(IntegrationsPageComponent);
    filters = TestBed.inject(IntegrationFilterService);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  async function renderedNames(): Promise<string[]> {
    fixture.detectChanges();
    await fixture.whenStable();
    return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.integration-name'))
      .map(element => element.textContent?.trim() ?? '');
  }

  const catalog: IpcIntegration[] = [
    integrationDto({
      id: 'a', name: 'Alpha', isInternal: true, enabled: true,
      providedCapabilities: [{ kind: 'actions', name: 'Actions' }, { kind: 'events', name: 'Events' }],
    }),
    integrationDto({
      id: 'b', name: 'Bravo', isInternal: false, enabled: true, issueCount: 2, issueSeverity: 'error',
      providedCapabilities: [{ kind: 'actions', name: 'Actions' }],
    }),
    integrationDto({
      id: 'c', name: 'Charlie', isInternal: false, enabled: false,
      providedCapabilities: [{ kind: 'events', name: 'Events' }],
    }),
  ];

  beforeEach(() => localStorage.removeItem(STORAGE_KEY));
  afterEach(() => localStorage.removeItem(STORAGE_KEY));

  it('shows everything alphabetically while no filter is set', async () => {
    await setup([...catalog].reverse());

    expect(await renderedNames()).toEqual(['Alpha', 'Bravo', 'Charlie']);
  });

  it('filters by enabled and by disabled state', async () => {
    await setup(catalog);

    filters.status.set('enabled');
    expect(await renderedNames()).toEqual(['Alpha', 'Bravo']);

    filters.status.set('disabled');
    expect(await renderedNames()).toEqual(['Charlie']);
  });

  it('filters by internal and by external type', async () => {
    await setup(catalog);

    filters.type.set('internal');
    expect(await renderedNames()).toEqual(['Alpha']);

    filters.type.set('external');
    expect(await renderedNames()).toEqual(['Bravo', 'Charlie']);
  });

  it('keeps only integrations providing every selected capability', async () => {
    await setup(catalog);

    filters.toggleCapability('events');
    expect(await renderedNames()).toEqual(['Alpha', 'Charlie']);

    filters.toggleCapability('actions');
    expect(await renderedNames()).toEqual(['Alpha']);
  });

  it('offers a capability option for every kind the host reported', async () => {
    await setup(catalog);

    const options = (fixture.componentInstance as unknown as FilterAccess).capabilityOptions();
    expect(options.map(option => option.kind)).toEqual(['actions', 'events']);
  });

  it('counts an incompatible plugin as having issues, not only a reported issue count', async () => {
    await setup(catalog, [{
      pluginId: 'c',
      displayName: 'Charlie',
      state: 'incompatible',
      usageSource: 'confirmed',
      usageTruncated: false,
      findings: [],
    } as PluginCompatibilityReport]);

    filters.issues.set('has');
    expect(await renderedNames()).toEqual(['Bravo', 'Charlie']);

    filters.issues.set('none');
    expect(await renderedNames()).toEqual(['Alpha']);
  });

  it('combines enabled, external and has-issues into one result', async () => {
    await setup(catalog);

    filters.status.set('enabled');
    filters.type.set('external');
    filters.issues.set('has');

    expect(await renderedNames()).toEqual(['Bravo']);
  });

  it('narrows the filtered set by the search term without persisting it', async () => {
    await setup(catalog);

    filters.type.set('external');
    (fixture.componentInstance as unknown as FilterAccess).search.set('char');

    expect(await renderedNames()).toEqual(['Charlie']);
    expect(localStorage.getItem(STORAGE_KEY)).not.toContain('char');
  });

  it('persists a facet selection for the next run', async () => {
    await setup(catalog);

    filters.status.set('disabled');
    await renderedNames();

    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}').status).toBe('disabled');
  });

  it('explains an empty result by the filters rather than by the search term', async () => {
    await setup(catalog);

    filters.status.set('enabled');
    filters.type.set('internal');
    filters.issues.set('has');

    expect(await renderedNames()).toEqual([]);
    const empty = fixture.nativeElement.querySelector('shared-empty-state').textContent as string;
    expect(empty).toContain('adjust the filters');
  });

  it('no longer offers the standalone sort control', async () => {
    await setup(catalog);

    expect(fixture.nativeElement.querySelector('.integrations-sort')).toBeNull();
    expect(fixture.nativeElement.querySelector('.integrations-filters')).not.toBeNull();
  });
});

interface FilterAccess {
  search: { set(value: string): void };
  capabilityOptions(): { kind: string }[];
}

interface InstallAccess {
  canUninstall(integration: { id: string; isInternal: boolean }): boolean;
  onArtifactPicked(input: HTMLInputElement): Promise<void>;
  confirmInstall(force?: boolean): Promise<void>;
  requestUninstall(integration: unknown): void;
  confirmUninstall(): Promise<void>;
  uninstallDeleteData: { set(value: boolean): void };
  pendingInstall(): unknown;
  replaceTarget(): unknown;
}

describe('IntegrationsPageComponent plugin installation', () => {
  let fixture: ComponentFixture<IntegrationsPageComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;

  function integrationDto(overrides: Partial<IpcIntegration>): IpcIntegration {
    return {
      id: 'app.test.integration',
      name: 'Test Integration',
      version: '1.0.0',
      isInternal: true,
      enabled: false,
      actionCount: 0,
      variableCount: 0,
      supportsConfigFlow: false,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 0,
      hasIcon: false,
      issueCount: 0,
      issueSeverity: null,
      ...overrides,
    } as IpcIntegration;
  }

  function installResponse(overrides: Record<string, unknown> = {}): PluginInstallActionResponse {
    return {
      success: true,
      pluginId: 'com.acme.deck-tools',
      version: '1.2.0',
      activated: true,
      rolledBack: false,
      warnings: [],
      publisher: { name: 'Acme Ltd' },
      signature: { verification: 'not_signed' },
      ...overrides,
    } as PluginInstallActionResponse;
  }

  async function setup(integrations: IpcIntegration[], installedPluginIds: string[] = []): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getIntegrations',
      'setIntegrationEnabled',
      'getIntegrationIconUrl',
      'startConfigFlow',
      'onNotification',
      'getPluginCompatibility',
      'getInstalledPlugins',
      'inspectPluginArtifact',
      'installPluginArtifact',
      'uninstallPlugin',
    ]);
    apiSpy.getIntegrations.and.resolveTo({ integrations });
    apiSpy.setIntegrationEnabled.and.resolveTo({ success: true });
    apiSpy.startConfigFlow.and.resolveTo({ supported: false });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: [] });
    apiSpy.getInstalledPlugins.and.resolveTo({
      plugins: installedPluginIds.map(pluginId => ({
        pluginId,
        name: pluginId,
        versions: [{ version: '1.2.0', active: true }],
        activeVersion: '1.2.0',
        permissions: [],
      })),
    });
    apiSpy.inspectPluginArtifact.and.resolveTo(installResponse());
    apiSpy.installPluginArtifact.and.resolveTo(installResponse());
    apiSpy.uninstallPlugin.and.resolveTo(installResponse());

    TestBed.configureTestingModule({
      imports: [IntegrationsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        IntegrationService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    fixture = TestBed.createComponent(IntegrationsPageComponent);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  function access(): InstallAccess {
    return fixture.componentInstance as unknown as InstallAccess;
  }

  function pickedInput(): HTMLInputElement {
    const input = document.createElement('input');
    input.type = 'file';
    const file = new File(['artifact'], 'deck-tools.macroDeckPlugin');
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    return input;
  }

  it('inspects the chosen artifact and installs nothing until the user confirms', async () => {
    await setup([]);

    await access().onArtifactPicked(pickedInput());

    expect(apiSpy.inspectPluginArtifact).toHaveBeenCalledTimes(1);
    expect(apiSpy.installPluginArtifact).not.toHaveBeenCalled();
    expect(access().pendingInstall()).not.toBeNull();
  });

  it('reports an inspect refusal from the response body instead of offering to install', async () => {
    await setup([]);
    apiSpy.inspectPluginArtifact.and.resolveTo(
      installResponse({ success: false, error: { code: 'invalid_archive', message: 'Not a readable archive.' } }));

    await access().onArtifactPicked(pickedInput());

    expect(access().pendingInstall()).toBeNull();
    expect(apiSpy.installPluginArtifact).not.toHaveBeenCalled();
  });

  it('retries with force only after a second confirmation when the version is already installed', async () => {
    await setup([]);
    apiSpy.installPluginArtifact.and.resolveTo(
      installResponse({ success: false, error: { code: 'already_installed', message: 'Already installed.' } }));

    await access().onArtifactPicked(pickedInput());
    await access().confirmInstall();

    expect(apiSpy.installPluginArtifact.calls.mostRecent().args[1]).toBeFalsy();
    expect(access().replaceTarget()).not.toBeNull();

    apiSpy.installPluginArtifact.and.resolveTo(installResponse());
    await access().confirmInstall(true);

    expect(apiSpy.installPluginArtifact.calls.mostRecent().args[1]).toBeTrue();
  });

  it('offers an uninstall only for integrations the host lists as installed artifacts', async () => {
    await setup(
      [
        integrationDto({ id: 'app.builtin', name: 'Built in', isInternal: true }),
        integrationDto({ id: 'com.acme.dev-token', name: 'Dev token plugin', isInternal: false }),
        integrationDto({ id: 'com.acme.deck-tools', name: 'Deck Tools', isInternal: false }),
      ],
      ['com.acme.deck-tools']);

    expect(access().canUninstall({ id: 'app.builtin', isInternal: true })).toBeFalse();
    expect(access().canUninstall({ id: 'com.acme.dev-token', isInternal: false })).toBeFalse();
    expect(access().canUninstall({ id: 'com.acme.deck-tools', isInternal: false })).toBeTrue();
  });

  it('keeps the plugin data unless the user asks for its removal', async () => {
    const integration = integrationDto({ id: 'com.acme.deck-tools', name: 'Deck Tools', isInternal: false });
    await setup([integration], ['com.acme.deck-tools']);

    access().requestUninstall(integration);
    await access().confirmUninstall();
    expect(apiSpy.uninstallPlugin.calls.mostRecent().args[1]).toEqual({ keepData: true });

    access().requestUninstall(integration);
    access().uninstallDeleteData.set(true);
    await access().confirmUninstall();
    expect(apiSpy.uninstallPlugin.calls.mostRecent().args[1]).toEqual({ keepData: false });
  });

  it('keeps the card and never force-retries when the host refuses the uninstall', async () => {
    const integration = integrationDto({ id: 'com.acme.deck-tools', name: 'Deck Tools', isInternal: false });
    await setup([integration], ['com.acme.deck-tools']);
    apiSpy.uninstallPlugin.and.resolveTo(installResponse({
      success: false,
      error: { code: 'dependency_in_use', message: "'com.acme.deck-tools' is required by com.other." },
    }));
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });

    access().requestUninstall(integration);
    await access().confirmUninstall();
    await fixture.whenStable();

    const service = TestBed.inject(IntegrationService);
    expect(service.integrations().some(i => i.id === 'com.acme.deck-tools')).toBeTrue();
    expect(apiSpy.uninstallPlugin.calls.count()).toBe(1);
    expect(apiSpy.uninstallPlugin.calls.mostRecent().args[1]).not.toEqual({ keepData: true, force: true });
  });

  it('drops the card once the host has uninstalled the plugin', async () => {
    const integration = integrationDto({ id: 'com.acme.deck-tools', name: 'Deck Tools', isInternal: false });
    await setup([integration], ['com.acme.deck-tools']);
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });

    access().requestUninstall(integration);
    await access().confirmUninstall();
    await fixture.whenStable();

    const service = TestBed.inject(IntegrationService);
    expect(service.integrations().some(i => i.id === 'com.acme.deck-tools')).toBeFalse();
    // Re-read from the host rather than spliced locally, so the page and the host cannot disagree.
    expect(apiSpy.getIntegrations.calls.count()).toBeGreaterThan(1);
  });
});

describe('IntegrationsPageComponent opened plugin artifact', () => {
  let fixture: ComponentFixture<IntegrationsPageComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;
  let pending: ReturnType<typeof signal<readonly { kind: string; path: string }[]>>;

  const ARTIFACT = '/tmp/Deck Tools.macroDeckPlugin';

  function installResponse(overrides: Record<string, unknown> = {}): PluginInstallActionResponse {
    return {
      success: true,
      pluginId: 'com.acme.deck-tools',
      version: '1.2.0',
      activated: true,
      rolledBack: false,
      warnings: [],
      publisher: { name: 'Acme Ltd' },
      signature: { verification: 'not_signed' },
      ...overrides,
    } as PluginInstallActionResponse;
  }

  async function setup(opened: readonly { kind: string; path: string }[] = []): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getIntegrations',
      'setIntegrationEnabled',
      'getIntegrationIconUrl',
      'startConfigFlow',
      'onNotification',
      'getPluginCompatibility',
      'getInstalledPlugins',
      'inspectPluginArtifact',
      'inspectPluginArtifactPath',
      'installPluginArtifact',
      'installPluginArtifactPath',
    ]);
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.startConfigFlow.and.resolveTo({ supported: false });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: [] });
    apiSpy.getInstalledPlugins.and.resolveTo({ plugins: [] });
    apiSpy.inspectPluginArtifactPath.and.resolveTo(installResponse());
    apiSpy.installPluginArtifactPath.and.resolveTo(installResponse());

    pending = signal<readonly { kind: string; path: string }[]>(opened);

    TestBed.configureTestingModule({
      imports: [IntegrationsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        IntegrationService,
        { provide: ApiService, useValue: apiSpy },
        {
          provide: FileOpenService,
          useValue: {
            pending,
            claim: (kind: string) => {
              const waiting = pending();
              const index = waiting.findIndex(entry => entry.kind === kind);
              if (index < 0) {
                return null;
              }
              pending.set(waiting.filter((_, position) => position !== index));
              return waiting[index];
            },
          },
        },
      ],
    });
    fixture = TestBed.createComponent(IntegrationsPageComponent);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  function access(): { pendingInstall(): unknown } {
    return fixture.componentInstance as unknown as { pendingInstall(): unknown };
  }

  function confirmModal(): HTMLElement | null {
    return fixture.nativeElement.querySelector('shared-plugin-install-confirm-modal');
  }

  it('inspects an artifact that was already waiting when the page opened', async () => {
    await setup([{ kind: 'plugin', path: ARTIFACT }]);

    expect(apiSpy.inspectPluginArtifactPath).toHaveBeenCalledOnceWith(ARTIFACT);
    // By path, never by upload: the shell hands over a path and the webview has no bytes to send.
    expect(apiSpy.inspectPluginArtifact).not.toHaveBeenCalled();
    expect(apiSpy.installPluginArtifactPath).not.toHaveBeenCalled();
    expect(access().pendingInstall()).not.toBeNull();
    expect(confirmModal()).not.toBeNull();
  });

  it('inspects an artifact opened while this page is already the open one', async () => {
    await setup();

    pending.set([{ kind: 'plugin', path: ARTIFACT }]);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();

    expect(apiSpy.inspectPluginArtifactPath).toHaveBeenCalledOnceWith(ARTIFACT);
    expect(confirmModal()).not.toBeNull();
    expect(pending()).toEqual([]);
  });

  it('installs by path once the user confirms', async () => {
    await setup([{ kind: 'plugin', path: ARTIFACT }]);

    await (fixture.componentInstance as unknown as { confirmInstall(): Promise<void> }).confirmInstall();

    // The third argument is the unsigned consent, and confirming without having been asked must never
    // send it: only the modal's own checkbox may turn it on.
    expect(apiSpy.installPluginArtifactPath).toHaveBeenCalledWith(ARTIFACT, false, false);
    expect(apiSpy.installPluginArtifact).not.toHaveBeenCalled();
  });

  it('leaves an archive of another kind for the page that imports it', async () => {
    await setup([{ kind: 'folder', path: '/tmp/Lights.macroDeckFolder' }]);

    expect(apiSpy.inspectPluginArtifactPath).not.toHaveBeenCalled();
    expect(confirmModal()).toBeNull();
    expect(pending()).toEqual([{ kind: 'folder', path: '/tmp/Lights.macroDeckFolder' }]);
  });
});

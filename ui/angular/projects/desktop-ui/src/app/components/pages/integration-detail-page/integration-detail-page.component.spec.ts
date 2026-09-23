import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, EMPTY } from 'rxjs';

import { ActionParameterType, CompatibilityFinding, ConfigEntryDto, GetIntegrationCapabilitiesResponse, IntegrationIssuesChangedEvent, IntegrationsChangedEvent, IpcIntegrationActionCapability, IpcIntegrationIssue, IpcIntegrationVariableCapability, InstalledPlugin, PluginCompatibilityReport, Variable } from '@macro-deck/runtime';
import { ApiService, ToastService, VariableService } from '@shared';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { Integration, IntegrationService } from '../../../services/integration.service';
import { PluginCompatibilityService } from '../../../services/plugin-compatibility.service';
import { StoreAccessService } from '../../../services/store-access.service';
import { PluginInstallationService } from '../../../services/plugin-installation.service';

import { IntegrationDetailPageComponent } from './integration-detail-page.component';

describe('IntegrationDetailPageComponent', () => {
  const integrations = signal<Integration[]>([]);
  const liveVariables = signal<Variable[]>([]);
  const compatibilityReports = signal<PluginCompatibilityReport[]>([]);
  const installedPlugins = signal<InstalledPlugin[]>([]);
  let entries: ConfigEntryDto[] = [];
  let issuesEvents: Subject<IntegrationIssuesChangedEvent>;
  let integrationsEvents: Subject<IntegrationsChangedEvent>;
  let cultureEvents: Subject<unknown>;
  let getLocalization: jasmine.Spy;
  let getIntegrationIssues: jasmine.Spy;
  let getIntegrationCapabilities: jasmine.Spy;
  let resolveIntegrationIssue: jasmine.Spy;
  let getStoreExtension: jasmine.Spy;
  let uninstallPlugin: jasmine.Spy;
  let storeUnlocked: ReturnType<typeof signal<boolean>>;
  let deleteConfigEntry: jasmine.Spy;
  let startConfigFlow: jasmine.Spy;
  let routerSpy: jasmine.SpyObj<Router>;
  let routeIntegrationId: string;
  let queryTab: string | null;

  function installedPlugin(overrides: Partial<InstalledPlugin> = {}): InstalledPlugin {
    return {
      pluginId: 'app.macro-deck.spotify',
      name: 'Spotify',
      versions: [{ version: '1.0.0', active: true }],
      activeVersion: '1.0.0',
      permissions: [],
      ...overrides,
    };
  }

  function integration(overrides: Partial<Integration> = {}): Integration {
    return {
      id: 'app.macro-deck.spotify',
      name: 'Spotify',
      version: '1.0.0',
      enabled: true,
      isInternal: true,
      hasIcon: false,
      actionCount: 0,
      variableCount: 0,
      supportsConfigFlow: true,
      allowsMultipleConfigurations: false,
      configuredEntryCount: 0,
      issueCount: 0,
      issueSeverity: null,
      isInitialized: true,
      variablesDependOnConfiguration: false,
      providedCapabilities: [],
      ...overrides,
    } as Integration;
  }

  function actionCapability(overrides: Partial<IpcIntegrationActionCapability> = {}): IpcIntegrationActionCapability {
    return {
      id: 'turn-on',
      name: 'Turn on',
      description: 'Turns the light on',
      parameterCount: 0,
      parameterSummary: [],
      availability: 'Ready',
      availabilityReason: 'Ready',
      ...overrides,
    };
  }

  function variableCapability(
    overrides: Partial<IpcIntegrationVariableCapability> = {},
  ): IpcIntegrationVariableCapability {
    return {
      name: 'brightness',
      type: 'numeric',
      decimalPlaces: null,
      refreshIntervalSeconds: null,
      isTemplate: false,
      availability: 'Ready',
      availabilityReason: 'Ready',
      value: '42',
      valueAvailable: true,
      ...overrides,
    };
  }

  function liveVariable(overrides: Partial<Variable> = {}): Variable {
    return {
      id: 'var-1',
      name: 'brightness',
      scope: 'global',
      type: 'numeric',
      classification: 'integration',
      ownerIntegrationId: 'app.macro-deck.spotify',
      value: '42',
      ...overrides,
    } as Variable;
  }

  function capabilitiesResponse(
    overrides: Partial<GetIntegrationCapabilitiesResponse> = {},
  ): GetIntegrationCapabilitiesResponse {
    return {
      found: true,
      enabled: true,
      isInitialized: true,
      supportsConfigFlow: true,
      requiresSetup: false,
      variablesDependOnConfiguration: false,
      configuredEntryCount: 1,
      actions: [],
      variables: [],
      ...overrides,
    };
  }

  async function createFixture(): Promise<ComponentFixture<IntegrationDetailPageComponent>> {
    const fixture = TestBed.createComponent(IntegrationDetailPageComponent);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function selectTab(fixture: ComponentFixture<IntegrationDetailPageComponent>, tab: 'overview' | 'actions' | 'variables'): void {
    const button = fixture.nativeElement.querySelector(`#integration-detail-tab-${tab}`) as HTMLButtonElement;
    button.click();
  }

  function cardTitled(
    fixture: ComponentFixture<IntegrationDetailPageComponent>,
    title: string,
  ): HTMLElement | undefined {
    return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.detail-card'))
      .find(card => card.querySelector('.detail-card-title')?.textContent?.trim() === title);
  }

  function badgeLabels(fixture: ComponentFixture<IntegrationDetailPageComponent>): string[] {
    return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('app-integration-status-strip .status-pill'))
      .map(pill => pill.textContent?.trim() ?? '');
  }

  function typeInSearch(fixture: ComponentFixture<IntegrationDetailPageComponent>, selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  afterEach(() => localStorage.clear());

  beforeEach(() => {
    // The localization catalog is cached in localStorage; a language another spec switched to must
    // not leak into these expectations.
    localStorage.clear();
    integrations.set([integration()]);
    liveVariables.set([]);
    compatibilityReports.set([]);
    installedPlugins.set([]);
    entries = [];
    routeIntegrationId = 'app.macro-deck.spotify';
    queryTab = null;
    issuesEvents = new Subject<IntegrationIssuesChangedEvent>();
    integrationsEvents = new Subject<IntegrationsChangedEvent>();
    cultureEvents = new Subject<unknown>();
    getLocalization = jasmine.createSpy('getLocalization').and.resolveTo({
      culture: 'en',
      fallbackCulture: 'en',
      translations: {},
      availableCultures: ['en', 'de'],
    });
    getIntegrationIssues = jasmine.createSpy('getIntegrationIssues').and.resolveTo({ issues: [] });
    getIntegrationCapabilities = jasmine.createSpy('getIntegrationCapabilities').and.resolveTo(capabilitiesResponse());
    resolveIntegrationIssue = jasmine
      .createSpy('resolveIntegrationIssue')
      .and.resolveTo({ success: true, followUp: 'None' });
    deleteConfigEntry = jasmine.createSpy('deleteConfigEntry').and.resolveTo({ success: true });
    getStoreExtension = jasmine.createSpy('getStoreExtension').and.resolveTo({ extension: null });
    storeUnlocked = signal(true);
    uninstallPlugin = jasmine.createSpy('uninstallPlugin').and.resolveTo({ success: true });
    startConfigFlow = jasmine.createSpy('startConfigFlow').and.resolveTo({
      supported: true,
      flowId: 'obs-flow',
      step: {
        stepId: 'connection',
        title: 'Connect to OBS Studio',
        fields: [
          { name: 'configurationName', type: ActionParameterType.String, label: 'Configuration name', description: '', required: true },
          { name: 'host', type: ActionParameterType.String, label: 'Host', description: '', required: true },
          { name: 'port', type: ActionParameterType.Number, label: 'Port', description: '', required: true },
          { name: 'password', type: ActionParameterType.Password, label: 'Password', description: '', required: false },
        ],
      },
      initialValues: { host: '127.0.0.1', port: 4455 },
    });
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: StoreAccessService, useValue: { unlocked: storeUnlocked } },
        {
          provide: IntegrationService,
          useValue: {
            integrations,
            isLoading: signal(false),
            loadIntegrations: async () => undefined,
            toggleIntegration: async () => undefined,
          },
        },
        {
          provide: ApiService,
          useValue: {
            getConfigEntries: async () => ({ entries }),
            getIntegrationIssues: (...args: unknown[]) => getIntegrationIssues(...args),
            getIntegrationCapabilities: (...args: unknown[]) => getIntegrationCapabilities(...args),
            resolveIntegrationIssue: (...args: unknown[]) => resolveIntegrationIssue(...args),
            getIntegrationIconUrl: () => null,
            getStoreExtension: (...args: unknown[]) => getStoreExtension(...args),
            // Keyed by name, the way the real transport is: handing every subscriber the issues
            // stream means any component that also listens for another event receives an
            // issues payload shaped as that event, which fails inside the wrong consumer.
            onNotification: (name: string) => {
              if (name === 'LocalizationCultureChangedEvent') {
                return cultureEvents.asObservable();
              }
              if (name === 'IntegrationsChangedEvent') {
                return integrationsEvents.asObservable();
              }
              return name === 'IntegrationIssuesChangedEvent' ? issuesEvents.asObservable() : EMPTY;
            },
            getLocalization: () => getLocalization(),
            startConfigFlow: (...args: unknown[]) => startConfigFlow(...args),
            deleteConfigEntry: (...args: unknown[]) => deleteConfigEntry(...args),
            renameConfigEntry: async () => ({ success: true }),
            // PluginCompatibilityService (issue #418) is a real, root-provided injectable the
            // compatibility card pulls in - its constructor effect reads this signal.
            connectionStateSignal: signal('connected'),
            getPluginCompatibility: async () => ({ plugins: [] }),
          },
        },
        { provide: VariableService, useValue: { variables: liveVariables } },
        {
          provide: PluginCompatibilityService,
          useValue: {
            reports: compatibilityReports,
            isLoading: signal(false),
            loadError: signal<string | null>(null),
            load: async () => undefined,
          },
        },
        {
          provide: PluginInstallationService,
          useValue: {
            find: (pluginId: string) => installedPlugins().find(p => p.pluginId === pluginId) ?? null,
            installedIds: () => new Set(installedPlugins().map(p => p.pluginId)),
            load: async () => undefined,
            uninstall: (...args: unknown[]) => uninstallPlugin(...args),
          },
        },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => routeIntegrationId },
              queryParamMap: { get: (key: string) => (key === 'tab' ? queryTab : null) },
            },
          },
        },
      ],
    });
  });

  it('offers setup for a single-configuration integration that has no entry yet', async () => {
    const fixture = await createFixture();

    const button = fixture.nativeElement.querySelector('shared-button');
    expect(button).not.toBeNull();
    expect(button.textContent).toContain('Set up');
    fixture.destroy();
  });

  it('offers reconfiguration once a single-configuration integration is set up', async () => {
    // Hiding the action here was what left re-authentication with no entry point at all, so a user
    // had to delete and re-add - which minted a new config entry id (issue #152).
    entries = [{ id: 'e1', title: 'Spotify' } as ConfigEntryDto];
    const fixture = await createFixture();

    const button = fixture.nativeElement.querySelector('shared-button');
    expect(button).not.toBeNull();
    expect(button.textContent).toContain('Reconfigure');
    fixture.destroy();
  });

  it('labels the setup action as adding another configuration for a multi-configuration integration', async () => {
    integrations.set([integration({ allowsMultipleConfigurations: true })]);
    entries = [{ id: 'e1', title: 'Instance' } as ConfigEntryDto];
    const fixture = await createFixture();

    const button = fixture.nativeElement.querySelector('shared-button');
    expect(button.textContent).toContain('Add configuration');
    fixture.destroy();
  });

  it('opens the normal config flow with name and connection fields when adding a configuration', async () => {
    routeIntegrationId = 'app.macro-deck.obs';
    integrations.set([integration({
      id: routeIntegrationId,
      name: 'OBS Studio',
      allowsMultipleConfigurations: true,
    })]);
    const fixture = await createFixture();

    const openButton = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === 'Add configuration')!;
    openButton.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(startConfigFlow).toHaveBeenCalledOnceWith('app.macro-deck.obs');
    const labels = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-config-field .cf-label'))
      .map(label => label.textContent?.trim());
    expect(labels).toEqual(['Configuration name *', 'Host *', 'Port *', 'Password']);
    fixture.destroy();
  });

  it('does not delete a configuration until the destructive confirmation is accepted', async () => {
    integrations.set([integration({ allowsMultipleConfigurations: true })]);
    entries = [{
      id: 'entry-a', title: 'Studio OBS', createdAt: '2026-08-22T00:00:00Z', status: 'Connected', usable: true,
    }];
    const fixture = await createFixture();

    (fixture.nativeElement.querySelector('.config-entry-remove') as HTMLButtonElement).click();
    fixture.detectChanges();
    let confirmation = fixture.debugElement.query(By.directive(ConfirmationModalComponent))
      .componentInstance as ConfirmationModalComponent;
    confirmation.onCancel();
    await new Promise(resolve => setTimeout(resolve, 180));
    expect(deleteConfigEntry).not.toHaveBeenCalled();

    (fixture.nativeElement.querySelector('.config-entry-remove') as HTMLButtonElement).click();
    fixture.detectChanges();
    confirmation = fixture.debugElement.query(By.directive(ConfirmationModalComponent))
      .componentInstance as ConfirmationModalComponent;
    confirmation.onConfirm();
    await new Promise(resolve => setTimeout(resolve, 180));

    expect(deleteConfigEntry).toHaveBeenCalledOnceWith('app.macro-deck.spotify', 'entry-a', true);
    fixture.destroy();
  });

  it('offers nothing when the integration has no config flow', async () => {
    integrations.set([integration({ supportsConfigFlow: false })]);
    const fixture = await createFixture();

    expect(fixture.nativeElement.querySelector('shared-button')).toBeNull();
    fixture.destroy();
  });

  it('shows the loading state while issues are being fetched', async () => {
    getIntegrationIssues.and.returnValue(new Promise(() => undefined));

    const fixture = await createFixture();

    expect(fixture.nativeElement.querySelector('shared-loading-state')).not.toBeNull();
    expect(badgeLabels(fixture)).not.toContain('Running cleanly');
    fixture.destroy();
  });

  it('shows an error banner instead of silently emptying the list on a failed load', async () => {
    getIntegrationIssues.and.rejectWith(new Error('boom'));
    const fixture = await createFixture();

    expect(fixture.nativeElement.querySelector('shared-error-banner')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeNull();
    expect(badgeLabels(fixture)).not.toContain('Running cleanly');
    fixture.destroy();
  });

  it('replaces the Issues card with a compact badge when an enabled integration has none', async () => {
    // Issue #537: a healthy integration must not spend a full-width card on an empty state.
    const fixture = await createFixture();

    expect(badgeLabels(fixture)).toContain('Running cleanly');
    expect(cardTitled(fixture, 'Issues')).toBeUndefined();
    fixture.destroy();
  });

  it('shows the Issues card and drops the badge once there are issues to inspect', async () => {
    getIntegrationIssues.and.resolveTo({
      issues: [{ id: 'i1', title: 'Needs attention', severity: 'error', actionLabel: 'Fix' }],
    });

    const fixture = await createFixture();

    expect(cardTitled(fixture, 'Issues')).toBeTruthy();
    expect(cardTitled(fixture, 'Issues')!.textContent).toContain('Needs attention');
    expect(badgeLabels(fixture)).not.toContain('Running cleanly');
    fixture.destroy();
  });

  it('shows no runtime badge at all for a disabled integration', async () => {
    integrations.set([integration({ enabled: false })]);

    const fixture = await createFixture();

    expect(cardTitled(fixture, 'Issues')).toBeUndefined();
    expect(badgeLabels(fixture)).not.toContain('Running cleanly');
    fixture.destroy();
  });

  describe('compatibility status', () => {
    function report(overrides: Partial<PluginCompatibilityReport> = {}): PluginCompatibilityReport {
      return {
        pluginId: 'app.macro-deck.spotify',
        displayName: 'Spotify',
        state: 'compatible',
        usageSource: 'confirmed',
        usageTruncated: false,
        findings: [],
        ...overrides,
      };
    }

    function finding(overrides: Partial<CompatibilityFinding> = {}): CompatibilityFinding {
      return {
        subject: 'IIntegration.Legacy',
        diagnosticId: 'MD0001',
        severity: 'warning',
        source: 'confirmed',
        guidance: 'Use the new API instead.',
        ...overrides,
      } as CompatibilityFinding;
    }

    beforeEach(() => {
      integrations.set([integration({ isInternal: false })]);
    });

    it('lists ADB among the capabilities of a plugin that uses it', async () => {
      installedPlugins.set([installedPlugin({ permissions: ['host:adb'] })]);

      const fixture = await createFixture();

      expect(badgeLabels(fixture)).not.toContain('Uses ADB');
      expect(cardTitled(fixture, 'Capabilities')!.textContent).toContain('Uses ADB');
      fixture.destroy();
    });

    it('does not mark a plugin that does not declare ADB', async () => {
      installedPlugins.set([installedPlugin({ permissions: ['host:deck'] })]);

      const fixture = await createFixture();

      expect(cardTitled(fixture, 'Capabilities')!.textContent).not.toContain('Uses ADB');
      fixture.destroy();
    });

    it('replaces the Compatibility card with a badge for a plugin with a clean verdict', async () => {
      compatibilityReports.set([report()]);

      const fixture = await createFixture();

      expect(badgeLabels(fixture)).toContain('Fully compatible');
      expect(cardTitled(fixture, 'Compatibility')).toBeUndefined();
      fixture.destroy();
    });

    it('shows the Compatibility card when the plugin has findings', async () => {
      compatibilityReports.set([report({ state: 'deprecated_apis', findings: [finding()] })]);

      const fixture = await createFixture();

      const card = cardTitled(fixture, 'Compatibility');
      expect(card).toBeTruthy();
      expect(card!.textContent).toContain('MD0001');
      expect(badgeLabels(fixture)).not.toContain('Fully compatible');
      fixture.destroy();
    });

    it('shows the Compatibility card for a non-compatible state even without findings', async () => {
      // The overall verdict is the warning here - collapsing it to "Fully compatible" would invert it.
      compatibilityReports.set([report({ state: 'update_required' })]);

      const fixture = await createFixture();

      expect(cardTitled(fixture, 'Compatibility')).toBeTruthy();
      expect(badgeLabels(fixture)).not.toContain('Fully compatible');
      fixture.destroy();
    });

    it('shows the Compatibility card when the usage manifest was truncated', async () => {
      compatibilityReports.set([report({ usageTruncated: true })]);

      const fixture = await createFixture();

      expect(cardTitled(fixture, 'Compatibility')).toBeTruthy();
      expect(badgeLabels(fixture)).not.toContain('Fully compatible');
      fixture.destroy();
    });

    it('reports a missing verdict as a neutral badge rather than a card', async () => {
      compatibilityReports.set([]);

      const fixture = await createFixture();

      expect(badgeLabels(fixture)).toContain('No compatibility data');
      expect(badgeLabels(fixture)).not.toContain('Fully compatible');
      expect(cardTitled(fixture, 'Compatibility')).toBeUndefined();
      fixture.destroy();
    });

    it('shows nothing about compatibility for an internal integration', async () => {
      integrations.set([integration({ isInternal: true })]);
      compatibilityReports.set([]);

      const fixture = await createFixture();

      expect(cardTitled(fixture, 'Compatibility')).toBeUndefined();
      expect(badgeLabels(fixture)).not.toContain('No compatibility data');
      expect(badgeLabels(fixture)).not.toContain('Fully compatible');
      fixture.destroy();
    });
  });

  describe('host-supplied issue text', () => {
    // Issue #706: these arrive as `$localized` references, and interpolating them raw rendered
    // "[object Object]" where the issue's own words belong.
    const SCOPE = 'macrodeck.app';
    const TITLE_KEY = 'Integrations.Keyboard.Issues.PermissionRequiredTitle';
    const DESCRIPTION_KEY = 'Integrations.Keyboard.Issues.PermissionRequiredDescription';
    const ACTION_KEY = 'Integrations.Keyboard.Issues.GrantPermissionAction';
    const OUTCOME_KEY = 'Integrations.Issues.AccessibilityPermissionGranted';

    function localizedIssue(): IpcIntegrationIssue {
      return {
        id: 'keyboard-permission',
        title: { $localized: { scope: SCOPE, key: TITLE_KEY } },
        description: { $localized: { scope: SCOPE, key: DESCRIPTION_KEY } },
        severity: 'info',
        actionLabel: { $localized: { scope: SCOPE, key: ACTION_KEY } },
      };
    }

    it('renders a localized issue in the active language', async () => {
      getIntegrationIssues.and.resolveTo({ issues: [localizedIssue()] });

      const fixture = await createFixture();

      const card = cardTitled(fixture, 'Issues')!;
      expect(card.textContent).toContain('Accessibility permission required');
      expect(card.textContent).toContain('macOS requires Accessibility permission');
      expect(card.textContent).not.toContain('[object Object]');
      expect(card.querySelector('shared-button')!.textContent).toContain('Grant permission');
      fixture.destroy();
    });

    it('re-renders it in the new language without asking the host for the issues again', async () => {
      getIntegrationIssues.and.resolveTo({ issues: [localizedIssue()] });
      const fixture = await createFixture();
      getIntegrationIssues.calls.reset();

      getLocalization.and.resolveTo({
        culture: 'de',
        fallbackCulture: 'en',
        translations: {
          [`${SCOPE}:${TITLE_KEY}`]: 'Bedienungshilfen-Berechtigung erforderlich',
          [`${SCOPE}:${DESCRIPTION_KEY}`]: 'macOS benötigt eine Bedienungshilfen-Berechtigung.',
          [`${SCOPE}:${ACTION_KEY}`]: 'Berechtigung erteilen',
        },
        availableCultures: ['en', 'de'],
      });
      cultureEvents.next({});
      await fixture.whenStable();
      fixture.detectChanges();
      await fixture.whenStable();

      const card = cardTitled(fixture, 'Issues')!;
      expect(card.textContent).toContain('Bedienungshilfen-Berechtigung erforderlich');
      expect(card.textContent).toContain('macOS benötigt eine Bedienungshilfen-Berechtigung.');
      expect(card.querySelector('shared-button')!.textContent).toContain('Berechtigung erteilen');
      expect(getIntegrationIssues).not.toHaveBeenCalled();
      fixture.destroy();
    });

    it('shows the resolve outcome the host reported, translated', async () => {
      // The response message is the only place the outcome of a resolve action exists - granted,
      // prompt raised, or System Settings opened - and it arrives as a `$localized` reference too
      // (issue #706).
      getIntegrationIssues.and.resolveTo({ issues: [localizedIssue()] });
      resolveIntegrationIssue.and.resolveTo({
        success: true,
        message: { $localized: { scope: SCOPE, key: OUTCOME_KEY } },
        followUp: 'None',
      });
      const fixture = await createFixture();

      await fixture.componentInstance.onResolveIssue(localizedIssue());

      expect(TestBed.inject(ToastService).toasts().map(toast => toast.message)).toEqual([
        'Accessibility permission granted.',
      ]);
      fixture.destroy();
    });

    it('renders plain text on the same properties unchanged', async () => {
      getIntegrationIssues.and.resolveTo({
        issues: [{ id: 'i1', title: 'Needs attention', severity: 'error', actionLabel: 'Fix' }],
      });

      const fixture = await createFixture();

      const card = cardTitled(fixture, 'Issues')!;
      expect(card.textContent).toContain('Needs attention');
      expect(card.querySelector('shared-button')!.textContent).toContain('Fix');
      fixture.destroy();
    });
  });

  it('applies a pushed issues event for the current integration without a refetch', async () => {
    const fixture = await createFixture();
    getIntegrationIssues.calls.reset();

    issuesEvents.next({
      integrationId: 'app.macro-deck.spotify',
      issues: [{ id: 'i1', title: 'Needs attention', severity: 'error', actionLabel: 'Fix' }],
      issueCount: 1,
      severity: 'error',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getIntegrationIssues).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Needs attention');
    fixture.destroy();
  });

  it('colours the issue badge for the worst severity actually in the list, not the overview summary', async () => {
    // integration.issueSeverity (the overview summary) deliberately disagrees with the fetched list,
    // to prove the badge derives its colour from issues() and not from the stale overview field.
    integrations.set([integration({ issueCount: 1, issueSeverity: 'warning' })]);
    getIntegrationIssues.and.resolveTo({
      issues: [
        { id: 'i1', title: 'Informational', severity: 'info', actionLabel: undefined },
        { id: 'i2', title: 'Broken', severity: 'error', actionLabel: 'Fix' },
      ],
    });

    const fixture = await createFixture();

    const badge = fixture.nativeElement.querySelector('.issue-badge');
    expect(badge).not.toBeNull();
    expect(badge.classList.contains('issue-badge-error')).toBeTrue();
    fixture.destroy();
  });

  it('renders info severity rather than falling back to warning for an info-only list', async () => {
    getIntegrationIssues.and.resolveTo({
      issues: [{ id: 'i1', title: 'Informational', severity: 'info', actionLabel: undefined }],
    });

    const fixture = await createFixture();

    const badge = fixture.nativeElement.querySelector('.issue-badge');
    expect(badge).not.toBeNull();
    expect(badge.classList.contains('issue-badge-info')).toBeTrue();
    fixture.destroy();
  });

  it('ignores a pushed issues event for a different integration', async () => {
    const fixture = await createFixture();

    issuesEvents.next({
      integrationId: 'app.other.integration',
      issues: [{ id: 'i1', title: 'Needs attention', severity: 'error', actionLabel: 'Fix' }],
      issueCount: 1,
      severity: 'error',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Needs attention');
    expect(badgeLabels(fixture)).toContain('Running cleanly');
    fixture.destroy();
  });

  describe('capability catalog', () => {
    it('renders the three tabs with their declared badge counts', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        actions: [actionCapability({ id: 'a1' }), actionCapability({ id: 'a2', name: 'Turn off' })],
        variables: [
          variableCapability({ name: 'v1' }),
          variableCapability({ name: 'v2' }),
          variableCapability({ name: 'v3' }),
        ],
      }));

      const fixture = await createFixture();

      const actionsBadge = fixture.nativeElement.querySelector('#integration-detail-tab-actions .tab-badge');
      const variablesBadge = fixture.nativeElement.querySelector('#integration-detail-tab-variables .tab-badge');
      expect(actionsBadge.textContent.trim()).toBe('2');
      expect(variablesBadge.textContent.trim()).toBe('3');
      fixture.destroy();
    });

    it('renders declared counts for a disabled integration', async () => {
      integrations.set([integration({ enabled: false })]);
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        enabled: false,
        actions: [actionCapability({ id: 'a1' }), actionCapability({ id: 'a2', name: 'Turn off' })],
        variables: [variableCapability({ name: 'v1' })],
      }));

      const fixture = await createFixture();

      const actionsBadge = fixture.nativeElement.querySelector('#integration-detail-tab-actions .tab-badge');
      const variablesBadge = fixture.nativeElement.querySelector('#integration-detail-tab-variables .tab-badge');
      expect(actionsBadge.textContent.trim()).toBe('2');
      expect(variablesBadge.textContent.trim()).toBe('1');
      fixture.destroy();
    });

    it('lists provider capabilities in the Overview Capabilities card and keeps the Actions/Variables counts', async () => {
      integrations.set([integration({
        providedCapabilities: [
          { kind: 'actions', name: 'Actions' },
          { kind: 'variables', name: 'Variables' },
          { kind: 'events', name: 'Events' },
          { kind: 'music-player', name: 'Music Player' },
        ],
      })]);
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        actions: [actionCapability({ id: 'a1' }), actionCapability({ id: 'a2', name: 'Turn off' })],
        variables: [variableCapability({ name: 'v1' })],
      }));

      const fixture = await createFixture();

      const cards = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.detail-card'));
      const capabilitiesCard = cards.find(card => card.textContent?.includes('Capabilities'));
      expect(capabilitiesCard).toBeTruthy();

      const labels = Array.from<HTMLElement>(capabilitiesCard!.querySelectorAll('.detail-label'))
        .map(label => label.textContent?.trim() ?? '');
      expect(labels).toContain('Music Player');
      expect(labels).toContain('Events');
      // Actions/Variables already have a counted row here, so they must not also render as a second row.
      expect(labels.filter(label => label === 'Actions').length).toBe(1);
      expect(labels.filter(label => label === 'Variables').length).toBe(1);

      const cardText = capabilitiesCard!.textContent ?? '';
      expect(cardText).toContain('2');
      expect(cardText).toContain('1');
      fixture.destroy();
    });

    it('shows an error rather than an empty catalog when the capabilities load fails', async () => {
      getIntegrationCapabilities.and.rejectWith(new Error('boom'));
      const fixture = await createFixture();

      selectTab(fixture, 'actions');
      await fixture.whenStable();
      fixture.detectChanges();

      // "We don't know" must not render as "this integration declares no actions".
      expect(fixture.nativeElement.querySelector('shared-error-banner')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeNull();
      fixture.destroy();
    });

    it('lists declared actions with their descriptions on the Actions tab', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        actions: [actionCapability({ name: 'Turn on', description: 'Turns the light on' })],
      }));
      const fixture = await createFixture();

      selectTab(fixture, 'actions');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.capability-row-name').textContent).toContain('Turn on');
      expect(fixture.nativeElement.querySelector('.capability-row-desc').textContent).toContain('Turns the light on');
      fixture.destroy();
    });

    it('filters actions by search and falls through to the no-matches empty state', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        actions: [actionCapability({ name: 'Turn on' }), actionCapability({ id: 'a2', name: 'Turn off' })],
      }));
      const fixture = await createFixture();

      selectTab(fixture, 'actions');
      await fixture.whenStable();
      fixture.detectChanges();

      typeInSearch(fixture, '.capability-search .control', 'nonexistent');
      await fixture.whenStable();
      fixture.detectChanges();

      const empty = fixture.nativeElement.querySelector('shared-empty-state');
      expect(empty).not.toBeNull();
      expect(empty.querySelector('.es-title').textContent).toBe('No matching actions');
      fixture.destroy();
    });

    it('shows "Available after setup" instead of a fabricated value and renders the template chip', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({
          name: 'twitch_<account>_follower_count',
          isTemplate: true,
          availability: 'AvailableAfterSetup',
          availabilityReason: 'Available after setup',
          value: null,
          valueAvailable: false,
        })],
      }));
      const fixture = await createFixture();

      selectTab(fixture, 'variables');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Available after setup');
      expect(fixture.nativeElement.querySelector('.template-chip').textContent)
        .toContain('One per configured account');
      fixture.destroy();
    });

    it('shows the live value for a ready variable', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({
          name: 'system_cpu_usage_percent',
          availability: 'Ready',
          availabilityReason: 'Ready',
          value: '42',
          valueAvailable: true,
        })],
      }));
      const fixture = await createFixture();

      selectTab(fixture, 'variables');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.capability-value').textContent).toContain('42');
      fixture.destroy();
    });

    it('tracks a live value change without refetching the catalog', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({ name: 'system_volume_percent', value: '13' })],
      }));
      liveVariables.set([liveVariable({ name: 'system_volume_percent', value: '13' })]);
      const fixture = await createFixture();

      selectTab(fixture, 'variables');
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.capability-value').textContent).toContain('13');

      liveVariables.set([liveVariable({ name: 'system_volume_percent', value: '77' })]);
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.capability-value').textContent).toContain('77');
      expect(getIntegrationCapabilities).toHaveBeenCalledTimes(1);
      fixture.destroy();
    });

    it('flips a ready row to Unavailable when the live variable stops being available', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({ name: 'system_volume_percent', value: '13' })],
      }));
      liveVariables.set([liveVariable({ name: 'system_volume_percent', available: false })]);
      const fixture = await createFixture();

      selectTab(fixture, 'variables');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.capability-value')).toBeNull();
      expect(fixture.nativeElement.querySelector('.availability-pill').textContent).toContain('Unavailable');
      fixture.destroy();
    });

    it('keeps the snapshot value for a variable the live store does not know', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({ name: 'system_volume_percent', value: '13' })],
      }));
      // A different integration's variable must not blank this row out.
      liveVariables.set([liveVariable({ name: 'other_thing', ownerIntegrationId: 'app.macro-deck.twitch' })]);
      const fixture = await createFixture();

      selectTab(fixture, 'variables');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.capability-value').textContent).toContain('13');
      fixture.destroy();
    });

    it('disables "Try it" without navigating for a non-ready action, and enables it for a ready one', async () => {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        actions: [
          actionCapability({
            id: 'blocked',
            name: 'Blocked action',
            availability: 'SetupRequired',
            availabilityReason: 'Setup required',
          }),
          actionCapability({ id: 'ready-action', name: 'Ready action' }),
        ],
      }));
      const fixture = await createFixture();

      selectTab(fixture, 'actions');
      await fixture.whenStable();
      fixture.detectChanges();

      const buttons = fixture.nativeElement.querySelectorAll('shared-button button');
      expect(buttons[0].disabled).toBeTrue();
      buttons[0].click();
      await fixture.whenStable();
      expect(routerSpy.navigate).not.toHaveBeenCalledWith(
        ['/developer'],
        jasmine.objectContaining({ queryParams: jasmine.objectContaining({ actionId: 'blocked' }) }),
      );

      expect(buttons[1].disabled).toBeFalse();
      buttons[1].click();
      await fixture.whenStable();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/developer'], {
        queryParams: { integrationId: 'app.macro-deck.spotify', actionId: 'ready-action' },
      });
      fixture.destroy();
    });

    it('launches setup from the Configuration card without hiding the catalog', async () => {
      const fixture = await createFixture();

      expect(fixture.nativeElement.querySelector('shared-tab-bar')).not.toBeNull();

      const button = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
        .find(b => b.textContent?.includes('Set up'));
      button!.click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('shared-config-flow-dialog')).not.toBeNull();
      fixture.destroy();
    });
  });

  describe('when the host announces an integration change', () => {
    const settle = async (fixture: ComponentFixture<IntegrationDetailPageComponent>) => {
      await new Promise(resolve => setTimeout(resolve));
      await fixture.whenStable();
      fixture.detectChanges();
    };

    const variableRows = (fixture: ComponentFixture<IntegrationDetailPageComponent>): string[] =>
      Array.from(fixture.nativeElement.querySelectorAll('app-variable-capability-row') as NodeListOf<HTMLElement>)
        .map(row => row.textContent ?? '');

    async function openVariables(): Promise<ComponentFixture<IntegrationDetailPageComponent>> {
      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({ name: 'alpha' })],
      }));
      const fixture = await createFixture();
      selectTab(fixture, 'variables');
      await settle(fixture);
      return fixture;
    }

    beforeEach(() => spyOn(console, 'error'));

    it('reloads the shown integration and lists the variable its new version declares', async () => {
      const fixture = await openVariables();

      getIntegrationCapabilities.and.resolveTo(capabilitiesResponse({
        variables: [variableCapability({ name: 'alpha' }), variableCapability({ name: 'beta' })],
      }));
      integrationsEvents.next({ integrationId: 'app.macro-deck.spotify' });
      await settle(fixture);

      const rows = variableRows(fixture);
      expect(rows.length).toBe(2);
      expect(rows.some(row => row.includes('beta'))).toBeTrue();
      fixture.destroy();
    });

    it('keeps the variables listed while the reload is pending', async () => {
      const fixture = await openVariables();

      getIntegrationCapabilities.and.returnValue(new Promise(() => undefined));
      integrationsEvents.next({ integrationId: 'app.macro-deck.spotify' });
      await settle(fixture);

      expect(variableRows(fixture).length).toBe(1);
      fixture.destroy();
    });

    it('keeps the last good catalog without an error banner when the reload fails', async () => {
      const fixture = await openVariables();

      getIntegrationCapabilities.and.rejectWith(new Error('host restarting'));
      integrationsEvents.next({ integrationId: 'app.macro-deck.spotify' });
      await settle(fixture);

      expect(variableRows(fixture).length).toBe(1);
      expect(fixture.nativeElement.querySelector('shared-error-banner')).toBeNull();
      fixture.destroy();
    });

    it('ignores a change to another integration', async () => {
      const fixture = await openVariables();
      const callsBefore = getIntegrationCapabilities.calls.count();

      integrationsEvents.next({ integrationId: 'app.macro-deck.obs' });
      await settle(fixture);

      expect(getIntegrationCapabilities.calls.count()).toBe(callsBefore);
      fixture.destroy();
    });
  });

  describe('store listing', () => {
    function storeButton(fixture: ComponentFixture<IntegrationDetailPageComponent>): HTMLElement | null {
      return (fixture.nativeElement as HTMLElement).querySelector('.detail-store-link button');
    }

    it('links a plugin the Store lists to its Store page, where its readme and versions are', async () => {
      integrations.set([integration({ isInternal: false })]);
      getStoreExtension.and.resolveTo({ extension: { id: 'app.macro-deck.spotify' } });
      const fixture = await createFixture();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(getStoreExtension).toHaveBeenCalledWith('Plugin', 'app.macro-deck.spotify');
      storeButton(fixture)!.click();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/store', 'Plugin', 'app.macro-deck.spotify']);
    });

    it('offers no Store link for a plugin the Store does not list', async () => {
      integrations.set([integration({ isInternal: false })]);
      const fixture = await createFixture();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(storeButton(fixture)).toBeNull();
    });

    it('keeps the Store link hidden while the Store is not open to this account', async () => {
      integrations.set([integration({ isInternal: false })]);
      getStoreExtension.and.resolveTo({ extension: { id: 'app.macro-deck.spotify' } });
      storeUnlocked.set(false);
      const fixture = await createFixture();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(storeButton(fixture)).toBeNull();
    });

    it('does not ask the Store about a built-in integration', async () => {
      const fixture = await createFixture();
      await fixture.whenStable();

      expect(getStoreExtension).not.toHaveBeenCalled();
      expect(storeButton(fixture)).toBeNull();
    });
  });

  describe('uninstall', () => {
    function uninstallButton(fixture: ComponentFixture<IntegrationDetailPageComponent>): HTMLElement | null {
      return (fixture.nativeElement as HTMLElement).querySelector('.detail-uninstall button');
    }

    function access(fixture: ComponentFixture<IntegrationDetailPageComponent>) {
      return fixture.componentInstance as unknown as {
        confirmUninstall(): Promise<void>;
        uninstallDeleteData: { set(value: boolean): void };
      };
    }

    async function installedExternal(): Promise<ComponentFixture<IntegrationDetailPageComponent>> {
      integrations.set([integration({ isInternal: false })]);
      installedPlugins.set([{ pluginId: 'app.macro-deck.spotify', activeVersion: '1.0.0', permissions: [] } as unknown as InstalledPlugin]);
      const fixture = await createFixture();
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture;
    }

    it('offers no uninstall for a built-in integration', async () => {
      const builtIn = await createFixture();

      expect(uninstallButton(builtIn)).toBeNull();
    });

    it('offers an uninstall in the general details for a plugin the host lists as installed', async () => {
      const fixture = await installedExternal();

      expect(uninstallButton(fixture)).not.toBeNull();
    });

    it('keeps the plugin data unless the user asks for its removal', async () => {
      const fixture = await installedExternal();

      uninstallButton(fixture)!.click();
      await access(fixture).confirmUninstall();
      expect(uninstallPlugin.calls.mostRecent().args).toEqual(['app.macro-deck.spotify', true]);

      uninstallButton(fixture)!.click();
      access(fixture).uninstallDeleteData.set(true);
      await access(fixture).confirmUninstall();
      expect(uninstallPlugin.calls.mostRecent().args).toEqual(['app.macro-deck.spotify', false]);
    });

    it('stays on the page and never retries when the host refuses the uninstall', async () => {
      const fixture = await installedExternal();
      uninstallPlugin.and.resolveTo({ success: false, error: { code: 'dependency_in_use', message: 'required by com.other' } });

      uninstallButton(fixture)!.click();
      await access(fixture).confirmUninstall();

      expect(uninstallPlugin.calls.count()).toBe(1);
      expect(routerSpy.navigate).not.toHaveBeenCalledWith(['/integrations']);
    });

    it('returns to the integrations list once the plugin is uninstalled', async () => {
      const fixture = await installedExternal();

      uninstallButton(fixture)!.click();
      await access(fixture).confirmUninstall();

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/integrations']);
    });
  });
});

import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { PairedPlugin, PluginAccessToken, PluginCompatibilityReport, PluginRuntimeInfo, PluginSessionInfo } from '@macro-deck/runtime';
import { DeveloperModeService } from '../../../../services/developer-mode.service';
import { PluginCompatibilityService } from '../../../../services/plugin-compatibility.service';
import { PluginPairingService } from '../../../../services/plugin-pairing.service';
import { PluginRuntimeService } from '../../../../services/plugin-runtime.service';
import { PluginTokenService } from '../../../../services/plugin-token.service';
import { PluginDevelopmentTabComponent } from './plugin-development-tab.component';
import { SettingsModalService } from '../../../../services/settings-modal.service';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';

function token(id: string, overrides: Partial<PluginAccessToken> = {}): PluginAccessToken {
  return {
    id,
    name: `Token ${id}`,
    scopes: [],
    createdAt: '2026-08-01T00:00:00Z',
    registrations: [],
    activeSessionCount: 0,
    ...overrides,
  };
}

function pairedPlugin(pluginId: string, overrides: Partial<PairedPlugin> = {}): PairedPlugin {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    createdAt: '2026-08-01T00:00:00Z',
    lastSeenAt: null,
    online: false,
    ...overrides,
  };
}

function runtimePlugin(pluginId: string, overrides: Partial<PluginRuntimeInfo> = {}): PluginRuntimeInfo {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    version: '1.0.0',
    state: 'running',
    health: 'healthy',
    managed: true,
    lastStopReason: 'none',
    consecutiveHealthFailures: 0,
    restartCount: 0,
    bootstrapOutput: [],
    ...overrides,
  };
}

function session(id: string, overrides: Partial<PluginSessionInfo> = {}): PluginSessionInfo {
  return {
    sessionId: id,
    pluginId: `plugin-${id}`,
    displayName: `Session ${id}`,
    tokenId: 't1',
    origin: 'self-registered',
    negotiatedVersion: 1,
    state: 'connected',
    connectedAt: '2026-08-01T00:00:00Z',
    lastSeenAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

function report(pluginId: string, overrides: Partial<PluginCompatibilityReport> = {}): PluginCompatibilityReport {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    state: 'update_required',
    usageSource: 'confirmed',
    usageTruncated: false,
    findings: [{
      diagnosticId: 'MDP5002',
      source: 'confirmed',
      severity: 'warning',
      subject: 'MacroDeck.Sdk.Foo.Bar',
      guidance: 'Use X instead.',
    }],
    ...overrides,
  };
}

interface Fixtures {
  tokens?: PluginAccessToken[];
  sessions?: PluginSessionInfo[];
  pairedPlugins?: PairedPlugin[];
  runtimePlugins?: PluginRuntimeInfo[];
  reports?: PluginCompatibilityReport[];
  developerMode?: boolean;
}

describe('PluginDevelopmentTabComponent', () => {
  let fixture: ComponentFixture<PluginDevelopmentTabComponent>;
  let queryParams: BehaviorSubject<ParamMap>;
  let tokenSpy: jasmine.SpyObj<PluginTokenService>;
  let pairingSpy: jasmine.SpyObj<PluginPairingService>;
  let runtimeSpy: jasmine.SpyObj<PluginRuntimeService>;
  let compatibilitySpy: jasmine.SpyObj<PluginCompatibilityService>;
  let developerModeEnabled: WritableSignal<boolean>;
  let router: jasmine.SpyObj<Router>;

  function configure(fixtures: Fixtures = {}): void {
    tokenSpy = jasmine.createSpyObj<PluginTokenService>(
      'PluginTokenService', ['load', 'createToken', 'revokeToken', 'deleteToken', 'terminateSession']);
    Object.defineProperty(tokenSpy, 'tokens', { value: signal(fixtures.tokens ?? []) });
    Object.defineProperty(tokenSpy, 'sessions', { value: signal(fixtures.sessions ?? []) });
    Object.defineProperty(tokenSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(tokenSpy, 'loadError', { value: signal<string | null>(null) });
    tokenSpy.load.and.resolveTo();

    pairingSpy = jasmine.createSpyObj<PluginPairingService>('PluginPairingService', ['load', 'revoke']);
    Object.defineProperty(pairingSpy, 'pairedPlugins', { value: signal(fixtures.pairedPlugins ?? []) });
    Object.defineProperty(pairingSpy, 'loadError', { value: signal<string | null>(null) });
    Object.defineProperty(pairingSpy, 'unavailable', { value: signal(false) });
    pairingSpy.load.and.resolveTo();

    runtimeSpy = jasmine.createSpyObj<PluginRuntimeService>(
      'PluginRuntimeService', ['load', 'start', 'stop', 'restart']);
    Object.defineProperty(runtimeSpy, 'plugins', { value: signal(fixtures.runtimePlugins ?? []) });
    Object.defineProperty(runtimeSpy, 'busy', { value: signal<ReadonlySet<string>>(new Set()) });
    Object.defineProperty(runtimeSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(runtimeSpy, 'loadError', { value: signal<string | null>(null) });
    runtimeSpy.load.and.resolveTo();

    compatibilitySpy = jasmine.createSpyObj<PluginCompatibilityService>(
      'PluginCompatibilityService', ['load', 'forPlugin']);
    Object.defineProperty(compatibilitySpy, 'reports', { value: signal(fixtures.reports ?? []) });
    Object.defineProperty(compatibilitySpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(compatibilitySpy, 'loadError', { value: signal<string | null>(null) });
    compatibilitySpy.load.and.resolveTo();

    const developerModeSpy = jasmine.createSpyObj<DeveloperModeService>('DeveloperModeService', ['ensureLoaded']);
    developerModeEnabled = signal(fixtures.developerMode ?? true);
    Object.defineProperty(developerModeSpy, 'enabled', { value: developerModeEnabled });
    developerModeSpy.ensureLoaded.and.resolveTo();

    queryParams = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    const routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);
    router = routerSpy;

    TestBed.configureTestingModule({
      imports: [PluginDevelopmentTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginTokenService, useValue: tokenSpy },
        { provide: PluginPairingService, useValue: pairingSpy },
        { provide: PluginRuntimeService, useValue: runtimeSpy },
        { provide: PluginCompatibilityService, useValue: compatibilitySpy },
        { provide: DeveloperModeService, useValue: developerModeSpy },
        { provide: ActivatedRoute, useValue: { queryParamMap: queryParams } },
        { provide: Router, useValue: routerSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<PluginDevelopmentTabComponent>> {
    const f = TestBed.createComponent(PluginDevelopmentTabComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  async function showSection(section: string): Promise<void> {
    queryParams.next(convertToParamMap({ section }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  const populated: Fixtures = {
    tokens: [token('t1', { name: 'CI bot' })],
    sessions: [session('s1', { displayName: 'Dev session' })],
    pairedPlugins: [pairedPlugin('paired-1', { displayName: 'Paired tools' })],
    runtimePlugins: [runtimePlugin('managed-1', { displayName: 'Managed tools' })],
    reports: [report('compat-1', { displayName: 'Compat tools' })],
  };

  it('starts on Managed plugins and offers all five sections in the rail', async () => {
    configure(populated);
    fixture = await create();

    const labels: string[] = Array.from(fixture.nativeElement.querySelectorAll('.rail-item-name'))
      .map((element) => ((element as HTMLElement).textContent ?? '').trim());
    expect(labels).toEqual(['Managed plugins', 'Paired plugins', 'Credentials', 'Sessions', 'Compatibility']);
    expect(fixture.componentInstance.activeSection()).toBe('managed-plugins');
  });

  it('resolves a bookmark from before the merge onto the section that tab used to be', async () => {
    configure(populated);
    fixture = await create();

    queryParams.next(convertToParamMap({ tab: 'plugin-tokens' }));
    expect(fixture.componentInstance.activeSection()).toBe('credentials');

    queryParams.next(convertToParamMap({ tab: 'managed-plugins' }));
    expect(fixture.componentInstance.activeSection()).toBe('managed-plugins');

    queryParams.next(convertToParamMap({ tab: 'compatibility' }));
    expect(fixture.componentInstance.activeSection()).toBe('compatibility');
  });

  // The legacy tab id outlives the first navigation, and selecting the default section drops the
  // `section` param. Unless the navigation also rewrites that id, the stale one maps the section
  // straight back and the default section is unreachable from a legacy bookmark.
  it('stays on the section picked after arriving from a legacy bookmark', async () => {
    configure(populated);
    fixture = await create();

    queryParams.next(convertToParamMap({ tab: 'plugin-tokens' }));
    expect(fixture.componentInstance.activeSection()).toBe('credentials');

    fixture.componentInstance.selectSection('managed-plugins');
    const navigated = router.navigate.calls.mostRecent().args[1]!.queryParams as Record<string, unknown>;
    queryParams.next(convertToParamMap(
      { tab: 'plugin-tokens', ...navigated } as Record<string, string>));

    expect(fixture.componentInstance.activeSection()).toBe('managed-plugins');
  });

  describe('each entity under its own section', () => {
    it('shows the supervised process under Managed plugins, and nothing else', async () => {
      configure(populated);
      fixture = await create();

      expect(text()).toContain('Managed tools');
      expect(text()).not.toContain('Paired tools');
      expect(text()).not.toContain('CI bot');
      expect(text()).not.toContain('Dev session');
    });

    it('shows the paired plugin under Paired plugins, and not the supervised process', async () => {
      configure(populated);
      fixture = await create();
      await showSection('paired-plugins');

      expect(text()).toContain('Paired tools');
      expect(text()).not.toContain('Managed tools');
    });

    it('shows the token under Credentials', async () => {
      configure(populated);
      fixture = await create();
      await showSection('credentials');

      expect(text()).toContain('CI bot');
      expect(text()).not.toContain('Managed tools');
    });

    it('shows the self-registered session under Sessions', async () => {
      configure(populated);
      fixture = await create();
      await showSection('sessions');

      expect(text()).toContain('Dev session');
      expect(text()).not.toContain('CI bot');
    });

    it('shows the compatibility finding under Compatibility', async () => {
      configure(populated);
      fixture = await create();
      await showSection('compatibility');

      expect(text()).toContain('MDP5002');
    });
  });

  describe('the actions of each section are distinguishable', () => {
    function rowActions(): string[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.plugin-tokens-actions button'))
        .map((button) => ((button as HTMLElement).textContent ?? '').trim());
    }

    it('offers Revoke and no process control on a credential', async () => {
      configure(populated);
      fixture = await create();
      await showSection('credentials');

      expect(rowActions()).toEqual(['Revoke']);
      expect(fixture.nativeElement.querySelector('.managed-plugins-menu-item')).toBeFalsy();
    });

    it('offers Start/Stop/Restart and no Revoke on a managed process', async () => {
      configure({ ...populated, pairedPlugins: [] });
      fixture = await create();
      fixture.componentInstance.activeSection.set('managed-plugins');
      const section = fixture.nativeElement.querySelector('app-managed-plugins-section');
      section.querySelector('.managed-plugins-actions button').click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const items: string[] = Array.from(fixture.nativeElement.querySelectorAll('.managed-plugins-menu-item'))
        .map((item) => ((item as HTMLElement).textContent ?? '').trim());
      expect(items).toEqual(['Start', 'Stop', 'Restart']);
      expect(rowActions()).not.toContain('Revoke');
    });

    it('offers End session and neither Revoke nor Remove on a session', async () => {
      configure(populated);
      fixture = await create();
      await showSection('sessions');

      expect(rowActions()).toEqual(['End session']);
    });
  });

  describe('Developer Mode off', () => {
    it('replaces the whole tab, rail included, with a single explanation', async () => {
      configure({ ...populated, developerMode: false });
      fixture = await create();

      expect((fixture.nativeElement.querySelector('.developer-mode-gate-heading') as HTMLElement).textContent)
        .toContain('Developer Mode is off');
      expect(fixture.nativeElement.querySelectorAll('app-developer-mode-gate').length).toBe(1);

      expect(fixture.nativeElement.querySelector('shared-rail-page')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('.rail-item')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('.managed-plugins-row')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('.plugin-tokens-row')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('.compatibility-card')).toBeFalsy();
      expect(text()).not.toContain('Managed tools');
      expect(text()).not.toContain('Paired tools');
      expect(text()).not.toContain('CI bot');
      expect(text()).not.toContain('Dev session');
      expect(text()).not.toContain('MDP5002');
    });

    it('fetches nothing at all', async () => {
      configure({ ...populated, developerMode: false });
      fixture = await create();

      expect(runtimeSpy.load).not.toHaveBeenCalled();
      expect(tokenSpy.load).not.toHaveBeenCalled();
      expect(pairingSpy.load).not.toHaveBeenCalled();
      expect(compatibilitySpy.load).not.toHaveBeenCalled();
    });

    it('lights the tab up in place when the toggle flips, fetching each source once', async () => {
      configure({ ...populated, developerMode: false });
      fixture = await create();

      developerModeEnabled.set(true);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('app-developer-mode-gate')).toBeFalsy();
      expect(text()).toContain('Managed tools');
      expect(runtimeSpy.load).toHaveBeenCalledTimes(1);
      expect(tokenSpy.load).toHaveBeenCalledTimes(1);
      expect(pairingSpy.load).toHaveBeenCalledTimes(1);
      expect(compatibilitySpy.load).toHaveBeenCalledTimes(1);

      await showSection('credentials');
      expect(text()).toContain('CI bot');
    });

    // The sections are destroyed with the rail, so a confirmation opened before the switch flipped
    // cannot survive on top of the gate and fire a request the host now refuses.
    it('takes an open confirmation away with the tab', async () => {
      configure(populated);
      fixture = await create();
      await showSection('credentials');

      (fixture.nativeElement.querySelector('.plugin-tokens-actions button') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

      developerModeEnabled.set(false);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeFalsy();
    });

    it('opens the Developer settings category from the gate', async () => {
      configure({ ...populated, developerMode: false });
      fixture = await create();
      const openSpy = spyOn(TestBed.inject(SettingsModalService), 'open');

      (fixture.nativeElement.querySelector('.developer-mode-gate shared-button button') as HTMLButtonElement).click();

      expect(openSpy).toHaveBeenCalledWith('developer');
    });
  });
});

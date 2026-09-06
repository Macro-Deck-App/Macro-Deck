import { NO_ERRORS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import {
  AppStrings,
  GetKeyRingStatusResponse,
} from '@macro-deck/runtime';
import {
  ApiService,
  AuthService,
  AuthState,
  ConnectionState,
  FolderService,
  KeyRingService,
  LocalizationService,
  ProfileService,
  ThemeService,
  VariableService,
} from '@shared';
import { AppComponent } from './app.component';
import { SplashComponent } from './components/splash';
import { FileOpenService, MenuActionService, NavigationService } from './services';

describe('AppComponent (desktop-ui) - lock screen never appears (issue #462)', () => {
  it('renders the normal shell with no lock-screen element while the host is authenticated and connected', async () => {
    const authState = signal<AuthState>('authenticated');
    const connectionState = signal<ConnectionState>('connected');

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            connectionStateSignal: connectionState.asReadonly(),
            connectionState$: new Subject<ConnectionState>(),
            connect: jasmine.createSpy('connect'),
            getAboutInfo: jasmine.createSpy('getAboutInfo').and.resolveTo({
              version: '0.0.0', isBeta: false, isDevelopmentBuild: true, commit: null,
              buildTimestamp: null, buildNumber: null, license: '', runtimeVersion: '', operatingSystem: '',
            }),
          },
        },
        {
          provide: AuthService,
          useValue: {
            state: authState.asReadonly(),
            bootstrap: jasmine.createSpy('bootstrap').and.resolveTo(undefined),
            startupProfileId: jasmine.createSpy('startupProfileId').and.returnValue(null),
          },
        },
        { provide: NavigationService, useValue: { setAppVersion: jasmine.createSpy('setAppVersion') } },
        { provide: FileOpenService, useValue: { start: jasmine.createSpy('start') } },
        { provide: MenuActionService, useValue: { start: jasmine.createSpy('start') } },
        { provide: ProfileService, useValue: { loadProfiles: jasmine.createSpy('loadProfiles').and.resolveTo(undefined), profiles: () => [] } },
        { provide: FolderService, useValue: {} },
        { provide: ThemeService, useValue: { loadFromHost: jasmine.createSpy('loadFromHost').and.resolveTo(undefined) } },
        { provide: LocalizationService, useValue: { loadFromHost: jasmine.createSpy('loadFromHost').and.resolveTo(undefined) } },
        { provide: VariableService, useValue: { loadVariables: jasmine.createSpy('loadVariables').and.resolveTo(undefined) } },
      ],
    })
      .overrideComponent(AppComponent, { set: { imports: [], schemas: [NO_ERRORS_SCHEMA] } })
      .compileComponents();

    const fixture = TestBed.createComponent(AppComponent);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('wc-lock-screen')).toBeNull();
    expect(element.querySelector('[class*="lock-screen"]')).toBeNull();
    expect(element.textContent?.toLowerCase()).not.toContain('computer locked');
  });
});

describe('AppComponent (desktop-ui) - key ring unlock gate (ADR 0047, issue #672)', () => {
  it('shows the unlock gate and never bootstraps auth or connects the API while the key ring is locked', async () => {
    const lockedStatus: GetKeyRingStatusResponse = {
      locked: true,
      lockReason: 'KeystoreEntryMissing',
      restartSupported: true,
      restartUnsupportedReason: null,
    };
    const connect = jasmine.createSpy('connect');
    const bootstrap = jasmine.createSpy('bootstrap').and.resolveTo(undefined);

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            connectionStateSignal: signal<ConnectionState>('disconnected').asReadonly(),
            connectionState$: new Subject<ConnectionState>(),
            connect,
            getAboutInfo: jasmine.createSpy('getAboutInfo'),
          },
        },
        {
          provide: AuthService,
          useValue: {
            state: signal<AuthState>('unknown').asReadonly(),
            bootstrap,
            startupProfileId: jasmine.createSpy('startupProfileId').and.returnValue(null),
          },
        },
        {
          provide: KeyRingService,
          useValue: {
            status: signal<GetKeyRingStatusResponse | null>(lockedStatus),
            locked: signal(true),
            probe: jasmine.createSpy('probe').and.resolveTo(undefined),
          },
        },
        { provide: NavigationService, useValue: { setAppVersion: jasmine.createSpy('setAppVersion') } },
        { provide: FileOpenService, useValue: { start: jasmine.createSpy('start') } },
        { provide: MenuActionService, useValue: { start: jasmine.createSpy('start') } },
        { provide: ProfileService, useValue: { loadProfiles: jasmine.createSpy('loadProfiles').and.resolveTo(undefined), profiles: () => [] } },
        { provide: FolderService, useValue: {} },
        { provide: ThemeService, useValue: { loadFromHost: jasmine.createSpy('loadFromHost').and.resolveTo(undefined) } },
        { provide: LocalizationService, useValue: { loadFromHost: jasmine.createSpy('loadFromHost').and.resolveTo(undefined) } },
        { provide: VariableService, useValue: { loadVariables: jasmine.createSpy('loadVariables').and.resolveTo(undefined) } },
      ],
    })
      .overrideComponent(AppComponent, { set: { imports: [], schemas: [NO_ERRORS_SCHEMA] } })
      .compileComponents();

    const fixture = TestBed.createComponent(AppComponent);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('app-key-ring-unlock-gate')).toBeTruthy();
    expect(element.querySelector('shared-login-form')).toBeNull();
    expect(element.querySelector('app-splash')).toBeNull();
    expect(connect).not.toHaveBeenCalled();
    expect(bootstrap).not.toHaveBeenCalled();
  });
});

describe('AppComponent (desktop-ui) - splash status during an update install (issue #869)', () => {
  const PREPARING_UPDATE = 'Preparing update…';
  const INSTALL_FAILED_DEFAULT = 'Couldn\'t install the update. Please try again.';
  const CONNECTING = 'Connecting to Macro Deck…';

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function setShell(shell: Record<string, unknown>): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = shell;
  }

  function makeState(overrides: Partial<ShellUpdateState> = {}): ShellUpdateState {
    return {
      phase: 'idle',
      supported: true,
      currentVersion: '3.0.0',
      version: null,
      notes: null,
      publishedAt: null,
      channel: 'stable',
      betaInstalled: false,
      installStrategy: 'inApp',
      downloadUrl: null,
      partialCheck: null,
      error: null,
      failure: null,
      progress: null,
      lastCheckedAt: null,
      ...overrides,
    };
  }

  function translateEnglish(key: string): string {
    const map: Record<string, string> = {
      [AppStrings.Shell.Splash.PreparingUpdate]: PREPARING_UPDATE,
      [AppStrings.Settings.Update.InstallFailed]: INSTALL_FAILED_DEFAULT,
      [AppStrings.WebClient.Connecting]: CONNECTING,
    };
    return map[key] ?? key;
  }

  async function createFixture(authState: AuthState, connectionState: ConnectionState) {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            connectionStateSignal: signal<ConnectionState>(connectionState).asReadonly(),
            connectionState$: new Subject<ConnectionState>(),
            connect: jasmine.createSpy('connect'),
            getAboutInfo: jasmine.createSpy('getAboutInfo').and.resolveTo({
              version: '0.0.0', isBeta: false, isDevelopmentBuild: true, commit: null,
              buildTimestamp: null, buildNumber: null, license: '', runtimeVersion: '', operatingSystem: '',
            }),
          },
        },
        {
          provide: AuthService,
          useValue: {
            state: signal<AuthState>(authState).asReadonly(),
            bootstrap: jasmine.createSpy('bootstrap').and.resolveTo(undefined),
            startupProfileId: jasmine.createSpy('startupProfileId').and.returnValue(null),
          },
        },
        {
          provide: KeyRingService,
          useValue: {
            status: signal<GetKeyRingStatusResponse | null>(null),
            locked: signal(false),
            probe: jasmine.createSpy('probe').and.resolveTo(undefined),
          },
        },
        { provide: NavigationService, useValue: { setAppVersion: jasmine.createSpy('setAppVersion') } },
        { provide: FileOpenService, useValue: { start: jasmine.createSpy('start') } },
        { provide: MenuActionService, useValue: { start: jasmine.createSpy('start') } },
        { provide: ProfileService, useValue: { loadProfiles: jasmine.createSpy('loadProfiles').and.resolveTo(undefined), profiles: () => [] } },
        { provide: FolderService, useValue: {} },
        { provide: ThemeService, useValue: { loadFromHost: jasmine.createSpy('loadFromHost').and.resolveTo(undefined) } },
        {
          provide: LocalizationService,
          useValue: {
            translateKey: jasmine.createSpy('translateKey').and.callFake(translateEnglish),
            loadFromHost: jasmine.createSpy('loadFromHost').and.resolveTo(undefined),
          },
        },
        { provide: VariableService, useValue: { loadVariables: jasmine.createSpy('loadVariables').and.resolveTo(undefined) } },
      ],
    })
      .overrideComponent(AppComponent, { set: { imports: [SplashComponent], schemas: [NO_ERRORS_SCHEMA] } })
      .compileComponents();

    const fixture = TestBed.createComponent(AppComponent);
    await settle(fixture);
    return fixture;
  }

  async function settle(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function statusText(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): string | null {
    return fixture.nativeElement.querySelector('app-splash .status .status-message')?.textContent ?? null;
  }

  it('A3: authenticated + reconnecting + installing shows the preparing-update message', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })),
    });

    const fixture = await createFixture('authenticated', 'reconnecting');

    expect(statusText(fixture)).toBe(PREPARING_UPDATE);
    expect(statusText(fixture)).not.toContain('Connecting');
  });

  it('A4: a failed install (attempted, then failed with an error) shows that error verbatim', async () => {
    let push!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'idle' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'disconnected');

    push(makeState({ phase: 'installing' }));
    await settle(fixture);
    push(makeState({ phase: 'failed', error: 'The update signature could not be verified.' }));
    await settle(fixture);

    expect(statusText(fixture)).toBe('The update signature could not be verified.');
    expect(statusText(fixture)).not.toContain('Couldn\'t install the update');
    expect(statusText(fixture)).not.toContain('Preparing update');
  });

  it('A5: a failed install with no error text falls back to the generic install-failed message', async () => {
    let push!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'idle' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'disconnected');

    push(makeState({ phase: 'installing' }));
    await settle(fixture);
    push(makeState({ phase: 'failed', error: null }));
    await settle(fixture);

    expect(statusText(fixture)).toBe(INSTALL_FAILED_DEFAULT);
  });

  it('A6: the @default branch (auth state unknown) shows the plain connecting message', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'idle' })),
    });

    const fixture = await createFixture('unknown', 'connecting');

    expect(statusText(fixture)).toBe(CONNECTING);
  });

  it('A7: connected + installing (the @default branch) still shows connecting, not preparing-update', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })),
    });

    const fixture = await createFixture('unknown', 'connected');

    expect(statusText(fixture)).toBe(CONNECTING);
    expect(statusText(fixture)).not.toContain('Preparing update');
  });

  it('A8: the message updates reactively as the phase moves from installing to failed', async () => {
    let push!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'reconnecting');

    expect(statusText(fixture)).toBe(PREPARING_UPDATE);

    push(makeState({ phase: 'failed', error: 'Installer exited with code 1.' }));
    await settle(fixture);

    expect(statusText(fixture)).toBe('Installer exited with code 1.');
    expect(statusText(fixture)).not.toContain('Preparing update');
  });

  it('A9: a failed update check (no install ever attempted) does not show install-failure wording', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'failed', error: 'The update feed could not be reached.' })),
    });

    const fixture = await createFixture('authenticated', 'disconnected');

    expect(statusText(fixture)).toBe(CONNECTING);
    expect(statusText(fixture)).not.toContain('The update feed could not be reached.');
  });
});

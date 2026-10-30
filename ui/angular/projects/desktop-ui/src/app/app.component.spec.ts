import { NO_ERRORS_SCHEMA, provideZonelessChangeDetection, signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import {
  AppStrings,
  GetBackupStatusResponse,
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
import { BackupService } from './services/backup.service';

describe('AppComponent (desktop-ui) - lock screen never appears (issue #462)', () => {
  const backupOperation = signal<GetBackupStatusResponse | null>(null);

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
        { provide: BackupService, useValue: { operation: backupOperation } },
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
  const backupOperation = signal<GetBackupStatusResponse | null>(null);

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
        { provide: BackupService, useValue: { operation: backupOperation } },
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
  let backupOperation: WritableSignal<GetBackupStatusResponse | null>;
  const PREPARING_UPDATE = 'Preparing update…';
  const INSTALL_FAILED_DEFAULT = 'Couldn\'t install the update. Please try again.';
  const CONNECTING = 'Connecting to Macro Deck…';
  const STOPPING = 'Stopping Macro Deck…';

  let backupFake: { operation: WritableSignal<GetBackupStatusResponse | null>; cancelOperation: jasmine.Spy };
  let connection: WritableSignal<ConnectionState>;

  beforeEach(() => {
    backupOperation = signal<GetBackupStatusResponse | null>(null);
    backupFake = { operation: backupOperation, cancelOperation: jasmine.createSpy('cancelOperation').and.resolveTo() };
  });

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
      notesUrl: null,
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
      installOnQuit: false,
      ...overrides,
    };
  }

  function translateEnglish(key: string, args?: Record<string, unknown>): string {
    const map: Record<string, string> = {
      [AppStrings.Shell.Splash.PreparingUpdate]: PREPARING_UPDATE,
      [AppStrings.Settings.Update.InstallFailed]: INSTALL_FAILED_DEFAULT,
      [AppStrings.WebClient.Connecting]: CONNECTING,
      [AppStrings.Shell.Splash.Stopping]: STOPPING,
      [AppStrings.Shell.Splash.CreatingBackup]: 'Creating a backup…',
      [AppStrings.Shell.Splash.CreatingBackupProgress]: 'Creating a backup… {percent}%',
      [AppStrings.Shell.Splash.InstallingUpdate]: 'Installing the update…',
      [AppStrings.Shell.Splash.StepBackup]: 'Back up your data',
      [AppStrings.Shell.Splash.StepInstall]: 'Install the update',
      [AppStrings.Shell.Splash.CancelUpdate]: 'Cancel update',
      [AppStrings.Store.EtaMinutesLeft]: 'about {count} min left',
    };
    const template = map[key] ?? key;
    return template.replace(/\{(\w+)\}/g, (match, name: string) => args && name in args ? String(args[name]) : match);
  }

  function backup(overrides: Partial<GetBackupStatusResponse> = {}): GetBackupStatusResponse {
    return {
      operationId: 'op-1',
      kind: 'Create',
      stage: 'Saving',
      trigger: 'BeforeHostUpdate',
      percentComplete: 42,
      updatedAt: '2026-10-24T20:00:00Z',
      ...overrides,
    };
  }

  function details(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): string[] {
    const spans = fixture.nativeElement.querySelectorAll('app-splash .status-detail > span:not(.status-separator)');
    return Array.from(spans as NodeListOf<HTMLElement>).map(span => span.textContent?.trim() ?? '');
  }

  function steps(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): string[] {
    const items = fixture.nativeElement.querySelectorAll('app-splash .status-step');
    return Array.from(items as NodeListOf<HTMLElement>).map(item =>
      `${item.textContent?.trim()}:${item.classList.contains('status-step-current') ? 'current' : item.classList.contains('status-step-done') ? 'done' : 'pending'}`);
  }

  function cancelButton(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): HTMLButtonElement | null {
    return fixture.nativeElement.querySelector('app-splash .cancel-button');
  }

  function progressValue(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): string | null {
    return fixture.nativeElement.querySelector('app-splash [role="progressbar"]')?.getAttribute('aria-valuenow') ?? null;
  }

  function installingShell(): void {
    setShell({ getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })) });
  }

  async function createFixture(authState: AuthState, connectionState: ConnectionState) {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            connectionStateSignal: (connection = signal<ConnectionState>(connectionState)).asReadonly(),
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
        { provide: BackupService, useValue: backupFake },
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

  it('A7: an install in progress shows the preparing-update message even before auth is known', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })),
    });

    const fixture = await createFixture('unknown', 'connected');

    expect(statusText(fixture)).toBe(PREPARING_UPDATE);
  });

  it('starting an install replaces the app with the preparing-update splash while the host is still connected', async () => {
    let push!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'downloaded' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'connected');
    expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-splash')).toBeNull();

    push(makeState({ phase: 'installing' }));
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('router-outlet')).toBeNull();
    expect(statusText(fixture)).toBe(PREPARING_UPDATE);
  });

  it('an install that fails before the host stops brings the app back', async () => {
    let push!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'connected');
    expect(statusText(fixture)).toBe(PREPARING_UPDATE);

    push(makeState({ phase: 'failed', error: 'The pre-update backup failed.' }));
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('app-splash')).toBeNull();
    expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
  });

  it('a quit announced during an install shows the stopping message while the host is still connected', async () => {
    let announce!: () => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'installing' })),
      onHostStopping: (callback: () => void) => {
        announce = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'connected');
    expect(statusText(fixture)).toBe(PREPARING_UPDATE);

    announce();
    await settle(fixture);

    expect(statusText(fixture)).toBe(STOPPING);
  });

  it('a quit announced by the shell turns the connecting message into the stopping message', async () => {
    let announce!: () => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'idle' })),
      onHostStopping: (callback: () => void) => {
        announce = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'reconnecting');
    expect(statusText(fixture)).toBe(CONNECTING);

    announce();
    await settle(fixture);

    expect(statusText(fixture)).toBe(STOPPING);
  });

  it('a quit after a failed install shows the stopping message, not the install error', async () => {
    let push!: (state: ShellUpdateState) => void;
    let announce!: () => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'idle' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
      onHostStopping: (callback: () => void) => {
        announce = callback;
        return Promise.resolve(() => {});
      },
    });

    const fixture = await createFixture('authenticated', 'disconnected');
    push(makeState({ phase: 'installing' }));
    await settle(fixture);
    push(makeState({ phase: 'failed', error: 'The update signature could not be verified.' }));
    await settle(fixture);

    announce();
    await settle(fixture);

    expect(statusText(fixture)).toBe(STOPPING);
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

  it('names the backup step with its real progress and remaining time while the pre-update backup runs', async () => {
    installingShell();
    const fixture = await createFixture('authenticated', 'connected');

    backupOperation.set(backup({ etaSeconds: 120 }));
    await settle(fixture);

    expect(statusText(fixture)).toBe('Creating a backup… 42%');
    expect(progressValue(fixture)).toBe('42');
    expect(details(fixture)).toEqual(['about 2 min left']);
    expect(steps(fixture)).toEqual(['Back up your data:current', 'Install the update:pending']);
  });

  it('cancels the bound pre-update backup from the splash', async () => {
    installingShell();
    const fixture = await createFixture('authenticated', 'connected');
    backupOperation.set(backup());
    await settle(fixture);

    cancelButton(fixture)!.click();

    expect(backupFake.cancelOperation).toHaveBeenCalledOnceWith('op-1');
  });

  it('shows an indeterminate spinner while the backup cannot report a percentage yet', async () => {
    installingShell();
    const fixture = await createFixture('authenticated', 'connected');

    backupOperation.set(backup({ stage: 'CreatingSnapshot', percentComplete: undefined }));
    await settle(fixture);

    expect(statusText(fixture)).toBe('Creating a backup…');
    expect(progressValue(fixture)).toBeNull();
    expect(fixture.nativeElement.querySelector('app-splash .spinner')).toBeTruthy();
  });

  it('moves on to the install step once the backup completed', async () => {
    installingShell();
    const fixture = await createFixture('authenticated', 'connected');
    backupOperation.set(backup());
    await settle(fixture);

    backupOperation.set(backup({ stage: 'Completed', percentComplete: 100 }));
    await settle(fixture);

    expect(statusText(fixture)).toBe('Installing the update…');
    expect(steps(fixture)).toEqual(['Back up your data:done', 'Install the update:current']);
    expect(cancelButton(fixture)).toBeNull();
  });

  it('does not take a finished backup from an earlier attempt for this install', async () => {
    backupOperation.set(backup({ operationId: 'earlier', stage: 'Completed', percentComplete: 100 }));
    installingShell();

    const fixture = await createFixture('authenticated', 'connected');

    expect(statusText(fixture)).toBe(PREPARING_UPDATE);
    expect(details(fixture)).toEqual([]);
  });

  it('follows a backup whose first event arrived before the installing phase', async () => {
    backupOperation.set(backup({ stage: 'Preparing', percentComplete: undefined }));
    installingShell();

    const fixture = await createFixture('authenticated', 'connected');
    backupOperation.set(backup({ percentComplete: 10 }));
    await settle(fixture);

    expect(statusText(fixture)).toBe('Creating a backup… 10%');
  });

  it('ignores a scheduled backup that runs while the update waits', async () => {
    installingShell();
    const fixture = await createFixture('authenticated', 'connected');

    backupOperation.set(backup({ operationId: 'scheduled', trigger: 'Scheduled' }));
    await settle(fixture);

    expect(statusText(fixture)).toBe(PREPARING_UPDATE);
    expect(progressValue(fixture)).toBeNull();
  });

  describe('after a connection drop (issue #1567)', () => {
    function outlet(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): HTMLElement | null {
      return fixture.nativeElement.querySelector('router-outlet');
    }

    function overlay(fixture: ReturnType<typeof TestBed.createComponent<AppComponent>>): HTMLElement | null {
      return fixture.nativeElement.querySelector('app-splash .splash-overlay');
    }

    it('shows the full splash and no page while the first connection is still being made', async () => {
      const fixture = await createFixture('authenticated', 'reconnecting');

      expect(outlet(fixture)).toBeNull();
      expect(fixture.nativeElement.querySelector('app-splash')).not.toBeNull();
      expect(overlay(fixture)).toBeNull();
      expect(statusText(fixture)).toBe(CONNECTING);
    });

    it('keeps the same page mounted under a connecting overlay and gives it back on reconnect', async () => {
      const fixture = await createFixture('authenticated', 'connected');
      const page = outlet(fixture);
      expect(page).not.toBeNull();
      expect(overlay(fixture)).toBeNull();

      connection.set('reconnecting');
      await settle(fixture);

      expect(outlet(fixture)).toBe(page);
      expect(overlay(fixture)).not.toBeNull();
      expect(statusText(fixture)).toBe(CONNECTING);
      expect(page!.parentElement!.inert).toBeTrue();

      connection.set('connected');
      await settle(fixture);

      expect(outlet(fixture)).toBe(page);
      expect(fixture.nativeElement.querySelector('app-splash')).toBeNull();
      expect(page!.parentElement!.inert).toBeFalse();
    });

    it('keeps page keyboard shortcuts from running while the page is hidden by the overlay', async () => {
      const fixture = await createFixture('authenticated', 'connected');
      const shortcut = jasmine.createSpy('shortcut');
      document.addEventListener('keydown', shortcut);

      try {
        connection.set('disconnected');
        await settle(fixture);
        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }));
        expect(shortcut).not.toHaveBeenCalled();

        connection.set('connected');
        await settle(fixture);
        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }));
        expect(shortcut).toHaveBeenCalledTimes(1);
      } finally {
        document.removeEventListener('keydown', shortcut);
        fixture.destroy();
      }
    });
  });
});

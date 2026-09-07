import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService, ConnectionState } from '@shared';
import { UpdateService, installErrorMessage } from './update.service';

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

describe('UpdateService', () => {
  let connectionState: WritableSignal<ConnectionState>;

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function setShell(shell: Record<string, unknown>): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = shell;
  }

  function createService(): UpdateService {
    connectionState = signal<ConnectionState>('disconnected');
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: { connectionStateSignal: connectionState } },
      ],
    });
    return TestBed.inject(UpdateService);
  }

  it('constructs without throwing when there is no shell bridge, and reports unsupported', () => {
    let service!: UpdateService;
    expect(() => {
      service = createService();
    }).not.toThrow();

    expect(service.hasBridge).toBe(false);
    expect(service.supported()).toBe(false);
  });

  it('seeds itself from getUpdateState and stays current through onUpdateState', async () => {
    let pushCallback!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'upToDate', currentVersion: '3.0.0' })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        pushCallback = callback;
        return Promise.resolve(() => {});
      },
    });

    const service = createService();
    await Promise.resolve();
    await Promise.resolve();

    expect(service.phase()).toBe('upToDate');

    pushCallback(makeState({ phase: 'available', version: '3.1.0' }));

    expect(service.phase()).toBe('available');
    expect(service.version()).toBe('3.1.0');
  });

  it('updates even while nothing is subscribed to render it - state is never lost between views', () => {
    let pushCallback!: (state: ShellUpdateState) => void;
    setShell({
      getUpdateState: () => Promise.resolve(makeState()),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        pushCallback = callback;
        return Promise.resolve(() => {});
      },
    });

    const service = createService();
    pushCallback(makeState({ phase: 'downloading', progress: { downloaded: 40, total: 100, percent: 40 } }));

    expect(service.progressPercent()).toBe(40);

    pushCallback(makeState({ phase: 'downloading', progress: { downloaded: 70, total: 100, percent: 70 } }));

    expect(service.progressPercent()).toBe(70);
  });

  it('checks for updates through the manual trigger, bypassing the shell rate limit', async () => {
    const checkForUpdate = jasmine.createSpy('checkForUpdate').and.resolveTo(undefined);
    setShell({ checkForUpdate });

    const service = createService();
    await service.check();

    expect(checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('is single-flight on install: two concurrent calls only reach installUpdate once', async () => {
    const install = jasmine.createSpy('installUpdate').and.returnValue(new Promise<void>(() => {}));
    setShell({ installUpdate: install });

    const service = createService();
    void service.install();
    void service.install();
    await Promise.resolve();

    expect(install).toHaveBeenCalledTimes(1);
  });

  it('cancels the download and returns to the available state with no progress', async () => {
    const cancel = jasmine.createSpy('cancelUpdateDownload').and.resolveTo(undefined);
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'downloading', progress: { downloaded: 10, total: 100, percent: 10 } }),
      ),
      cancelUpdateDownload: cancel,
    });

    const service = createService();
    await Promise.resolve();
    await Promise.resolve();

    await service.cancelDownload();

    expect(cancel).toHaveBeenCalledTimes(1);
    expect(service.phase()).toBe('available');
    expect(service.progressPercent()).toBeNull();
  });

  it('re-checks through requestUpdateCheck when the connection transitions to connected', () => {
    const requestUpdateCheck = jasmine.createSpy('requestUpdateCheck').and.resolveTo(undefined);
    setShell({ requestUpdateCheck });

    createService();
    expect(requestUpdateCheck).not.toHaveBeenCalled();

    connectionState.set('connected');
    TestBed.tick();

    expect(requestUpdateCheck).toHaveBeenCalled();
  });

  it('leaves an install failure in the failed phase with the shell-provided reason', async () => {
    const reason = 'The Macro Deck host could not be stopped, so the update was not installed.';
    setShell({ installUpdate: () => Promise.reject(reason) });

    const service = createService();
    await service.install();

    expect(service.phase()).toBe('failed');
    expect(service.error()).toBe(reason);
  });
});

describe('installErrorMessage', () => {
  it('surfaces a string rejection verbatim', () => {
    expect(installErrorMessage('could not stop the host')).toBe('could not stop the host');
  });

  it('returns null for a non-string rejection', () => {
    expect(installErrorMessage(new Error('boom'))).toBeNull();
  });

  it('returns null for a blank string', () => {
    expect(installErrorMessage('   ')).toBeNull();
  });
});

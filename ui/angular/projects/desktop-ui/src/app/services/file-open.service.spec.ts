import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ToastService } from '@shared';
import { IconPackService } from './icon-pack.service';

import { FileOpenService } from './file-open.service';
import { provideLocalizationTesting } from '../../testing/localization-test-support';

describe('FileOpenService', () => {
  let service: FileOpenService;
  let router: jasmine.SpyObj<Router>;
  let iconPacks: jasmine.SpyObj<IconPackService>;
  let toasts: jasmine.SpyObj<ToastService>;

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  function settle(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve));
  }

  function create(): void {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    iconPacks = jasmine.createSpyObj<IconPackService>('IconPackService', ['restoreFromPath']);
    iconPacks.restoreFromPath.and.resolveTo('Neon');
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: Router, useValue: router },
        { provide: IconPackService, useValue: iconPacks },
        { provide: ToastService, useValue: toasts },
      ],
    });
    service = TestBed.inject(FileOpenService);
  }

  beforeEach(create);

  afterEach(() => useShell(null));

  it('queues an opened profile for the selector to claim', async () => {
    await service.accept(['/tmp/Streaming.macroDeckProfile']);

    expect(service.pending()).toEqual([{ kind: 'profile', path: '/tmp/Streaming.macroDeckProfile' }]);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('navigates to the deck before queueing a folder or widget archive', async () => {
    await service.accept(['/tmp/Lights.macroDeckFolder']);

    expect(router.navigate).toHaveBeenCalledWith(['/deck']);
    expect(service.pending()).toEqual([{ kind: 'folder', path: '/tmp/Lights.macroDeckFolder' }]);
  });

  it('navigates to integrations before queueing a plugin artifact', async () => {
    await service.accept(['/tmp/Sample.macroDeckPlugin']);

    expect(router.navigate).toHaveBeenCalledWith(['/integrations']);
    expect(service.claim('plugin')?.path).toBe('/tmp/Sample.macroDeckPlugin');
  });

  it('imports an opened icon pack directly, without queueing it', async () => {
    await service.accept(['/tmp/Neon.macroDeckIconPack']);

    expect(iconPacks.restoreFromPath).toHaveBeenCalledWith('/tmp/Neon.macroDeckIconPack');
    expect(toasts.show).toHaveBeenCalledWith('Icon pack imported', { detail: 'Neon' });
    expect(service.pending()).toEqual([]);
  });

  it('reports a failed icon pack import', async () => {
    iconPacks.restoreFromPath.and.resolveTo(null);

    await service.accept(['/tmp/Neon.macroDeckIconPack']);

    expect(toasts.show).toHaveBeenCalledWith('Icon pack import failed', { variant: 'error' });
  });

  it('ignores a file that is not one of its archive types', async () => {
    await service.accept(['/tmp/notes.txt', '/tmp/pack.zip']);

    expect(service.pending()).toEqual([]);
    expect(iconPacks.restoreFromPath).not.toHaveBeenCalled();
  });

  it('claims only the matching kind, leaving the rest queued', async () => {
    await service.accept(['/tmp/a.macroDeckProfile', '/tmp/b.macroDeckFolder']);

    expect(service.claim('folder')).toEqual({ kind: 'folder', path: '/tmp/b.macroDeckFolder' });
    expect(service.pending()).toEqual([{ kind: 'profile', path: '/tmp/a.macroDeckProfile' }]);
    expect(service.claim('widgets')).toBeNull();
  });

  it('keeps more than one archive of the same kind', async () => {
    await service.accept(['/tmp/a.macroDeckProfile', '/tmp/b.macroDeckProfile']);

    expect(service.claim('profile')?.path).toBe('/tmp/a.macroDeckProfile');
    expect(service.claim('profile')?.path).toBe('/tmp/b.macroDeckProfile');
    expect(service.claim('profile')).toBeNull();
  });

  it('subscribes before draining the shell queue', async () => {
    const calls: string[] = [];
    useShell({
      onFileOpen: () => {
        calls.push('subscribe');
        return Promise.resolve(() => undefined);
      },
      takeOpenedFiles: () => {
        calls.push('drain');
        return Promise.resolve([]);
      },
    });

    service.start();

    expect(calls).toEqual(['subscribe', 'drain']);
  });

  it('imports whatever was already waiting when it started', async () => {
    useShell({
      onFileOpen: () => Promise.resolve(() => undefined),
      takeOpenedFiles: () => Promise.resolve(['/tmp/Streaming.macroDeckProfile']),
    });

    service.start();
    await settle();

    expect(service.pending()).toEqual([{ kind: 'profile', path: '/tmp/Streaming.macroDeckProfile' }]);
  });

  it('imports a plugin the OS opened before it was listening', async () => {
    // The shell queue outlives the startup the UI spends unauthenticated and disconnected, so the
    // first take is what delivers a file opened by double-clicking it (issue #608).
    useShell({
      onFileOpen: () => Promise.resolve(() => undefined),
      takeOpenedFiles: () => Promise.resolve(['/tmp/Sample.macroDeckPlugin']),
    });

    service.start();
    await settle();

    expect(router.navigate).toHaveBeenCalledWith(['/integrations']);
    expect(service.pending()).toEqual([{ kind: 'plugin', path: '/tmp/Sample.macroDeckPlugin' }]);
  });

  it('takes the waiting files when the shell signals, the signal carrying none itself', async () => {
    let signal: (() => void) | null = null;
    let waiting: string[] = [];
    useShell({
      onFileOpen: (callback: () => void) => {
        signal = callback;
        return Promise.resolve(() => undefined);
      },
      takeOpenedFiles: () => {
        const taken = waiting;
        waiting = [];
        return Promise.resolve(taken);
      },
    });

    service.start();
    await settle();

    waiting = ['/tmp/Sample.macroDeckPlugin'];
    signal!();
    await settle();

    expect(service.pending()).toEqual([{ kind: 'plugin', path: '/tmp/Sample.macroDeckPlugin' }]);
  });

  it('queues a plugin even when the app is already on the integrations page', async () => {
    // Navigating to the route the user is already on does nothing, so nothing may depend on it.
    router.navigate.and.resolveTo(false);

    await service.accept(['/tmp/Sample.macroDeckPlugin']);

    expect(service.pending()).toEqual([{ kind: 'plugin', path: '/tmp/Sample.macroDeckPlugin' }]);
  });

  it('subscribes only once however often it is started', () => {
    let subscriptions = 0;
    useShell({
      onFileOpen: () => {
        subscriptions++;
        return Promise.resolve(() => undefined);
      },
      takeOpenedFiles: () => Promise.resolve([]),
    });

    service.start();
    service.start();

    expect(subscriptions).toBe(1);
  });

  it('stays inert outside the desktop shell', () => {
    useShell(null);

    expect(() => service.start()).not.toThrow();
    expect(service.pending()).toEqual([]);
  });
});

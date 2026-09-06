import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { MenuActionService } from './menu-action.service';
import { SettingsModalService } from './settings-modal.service';

describe('MenuActionService', () => {
  let service: MenuActionService;
  let settingsModal: SettingsModalService;

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
    service = TestBed.inject(MenuActionService);
    settingsModal = TestBed.inject(SettingsModalService);
  });

  afterEach(() => useShell(null));

  it('opens the settings modal when the menu asks for it', () => {
    service.apply('settings');

    expect(settingsModal.isOpen()).toBeTrue();
  });

  it('ignores an action it does not know, and an empty slot', () => {
    service.apply('nonsense');
    service.apply(null);

    expect(settingsModal.isOpen()).toBeFalse();
  });

  it('subscribes before draining the shell slot', () => {
    const calls: string[] = [];
    useShell({
      onMenuAction: () => {
        calls.push('subscribe');
        return Promise.resolve(() => undefined);
      },
      takeMenuAction: () => {
        calls.push('drain');
        return Promise.resolve(null);
      },
    });

    service.start();

    expect(calls).toEqual(['subscribe', 'drain']);
  });

  it('applies whatever was already waiting when it started', async () => {
    useShell({
      onMenuAction: () => Promise.resolve(() => undefined),
      takeMenuAction: () => Promise.resolve('settings'),
    });

    service.start();
    await Promise.resolve();
    await Promise.resolve();

    expect(settingsModal.isOpen()).toBeTrue();
  });

  it('applies an action emitted while it is running', () => {
    let emit: ((event: { action: 'settings' }) => void) | null = null;
    useShell({
      onMenuAction: (callback: (event: { action: 'settings' }) => void) => {
        emit = callback;
        return Promise.resolve(() => undefined);
      },
      takeMenuAction: () => Promise.resolve(null),
    });

    service.start();
    emit!({ action: 'settings' });

    expect(settingsModal.isOpen()).toBeTrue();
  });

  it('subscribes only once however often it is started', () => {
    let subscriptions = 0;
    useShell({
      onMenuAction: () => {
        subscriptions++;
        return Promise.resolve(() => undefined);
      },
      takeMenuAction: () => Promise.resolve(null),
    });

    service.start();
    service.start();

    expect(subscriptions).toBe(1);
  });

  // A plain browser has no bridge at all; starting there must not throw.
  it('stays idle outside the desktop shell', () => {
    useShell(null);

    expect(() => service.start()).not.toThrow();
    expect(settingsModal.isOpen()).toBeFalse();
  });
});

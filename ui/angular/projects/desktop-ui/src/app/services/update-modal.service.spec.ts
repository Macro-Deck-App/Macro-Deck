import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService } from '@shared';
import { signal } from '@angular/core';
import { UpdateModalService } from './update-modal.service';
import { UpdateService } from './update.service';

function makeState(overrides: Partial<ShellUpdateState> = {}): ShellUpdateState {
  return {
    phase: 'downloaded',
    supported: true,
    currentVersion: '3.1.0',
    version: '3.2.0',
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
    installOnQuit: false,
    ...overrides,
  };
}

describe('UpdateModalService', () => {
  let push: (state: ShellUpdateState) => void;

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function setShell(shell: Record<string, unknown> = {}): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      getUpdateState: () => Promise.resolve(makeState({ phase: 'idle', version: null })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        push = callback;
        return Promise.resolve(() => {});
      },
      ...shell,
    };
  }

  async function settle(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve));
    TestBed.tick();
  }

  function createService(): UpdateModalService {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: { connectionStateSignal: signal('disconnected') } },
      ],
    });
    TestBed.inject(UpdateService);
    return TestBed.inject(UpdateModalService);
  }

  it('never opens by itself when an automatic download finishes', async () => {
    setShell();
    const service = createService();
    await settle();

    push(makeState({ installOnQuit: true }));
    await settle();

    expect(service.isOpen()).toBeFalse();
  });
});

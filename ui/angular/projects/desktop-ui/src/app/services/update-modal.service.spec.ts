import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService } from '@shared';
import { signal } from '@angular/core';
import { PostUpdateChangelogService } from './post-update-changelog.service';
import { UpdateModalService } from './update-modal.service';

function makeState(overrides: Partial<ShellUpdateState> = {}): ShellUpdateState {
  return {
    phase: 'downloaded',
    supported: true,
    currentVersion: '3.1.0',
    version: '3.2.0',
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
    autoInstallAt: null,
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
    return TestBed.inject(UpdateModalService);
  }

  it('opens by itself when an automatic install countdown starts', async () => {
    setShell();
    const service = createService();
    await settle();
    expect(service.isOpen()).toBeFalse();

    push(makeState({ autoInstallAt: 1_000 }));
    await settle();

    expect(service.isOpen()).toBeTrue();
  });

  it('does not reopen for the same countdown after the user closed it, but does for a new one', async () => {
    setShell();
    const service = createService();
    await settle();
    push(makeState({ autoInstallAt: 1_000 }));
    await settle();

    service.close();
    push(makeState({ autoInstallAt: 1_000 }));
    await settle();
    expect(service.isOpen()).toBeFalse();

    push(makeState({ autoInstallAt: 2_000 }));
    await settle();
    expect(service.isOpen()).toBeTrue();
  });

  it('waits for the what\'s-new dialog of the previous update to be closed first', async () => {
    setShell({
      getPostUpdateChangelog: () => Promise.resolve({ version: '3.1.0', notes: 'notes', notesUrl: null, publishedAt: null }),
      dismissPostUpdateChangelog: () => Promise.resolve(),
    });
    const service = createService();
    const changelog = TestBed.inject(PostUpdateChangelogService);
    await changelog.load();
    await settle();

    push(makeState({ autoInstallAt: 1_000 }));
    await settle();
    expect(service.isOpen()).toBeFalse();

    changelog.dismiss();
    await settle();
    expect(service.isOpen()).toBeTrue();
  });

  it('waits while the what\'s-new notes of the previous update are still being fetched', async () => {
    let answer!: (changelog: ShellPostUpdateChangelog | null) => void;
    setShell({
      getPostUpdateChangelog: () => new Promise<ShellPostUpdateChangelog | null>(resolve => { answer = resolve; }),
      dismissPostUpdateChangelog: () => Promise.resolve(),
    });
    const service = createService();
    const changelog = TestBed.inject(PostUpdateChangelogService);
    const load = changelog.load();
    await settle();

    push(makeState({ autoInstallAt: 1_000 }));
    await settle();
    expect(service.isOpen()).toBeFalse();

    answer(null);
    await load;
    await settle();
    expect(service.isOpen()).toBeTrue();
  });
});

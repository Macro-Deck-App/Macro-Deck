import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PostUpdateChangelogService } from './post-update-changelog.service';

describe('PostUpdateChangelogService', () => {
  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function setShell(shell: Record<string, unknown>): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = shell;
  }

  function createService(): PostUpdateChangelogService {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    return TestBed.inject(PostUpdateChangelogService);
  }

  it('offers the changelog the shell remembered for the version that was just installed', async () => {
    const changelog = { version: '3.2.0', notes: '## Fixes', notesUrl: null, publishedAt: '2026-09-16' };
    setShell({ getPostUpdateChangelog: () => Promise.resolve(changelog) });
    const service = createService();

    await service.load();

    expect(service.isOpen()).toBeTrue();
    expect(service.changelog()).toEqual(changelog);
  });

  it('is loading while the shell fetches the notes and stops loading when it fails', async () => {
    let fail!: (error: unknown) => void;
    setShell({ getPostUpdateChangelog: () => new Promise((_, reject) => { fail = reject; }) });
    const service = createService();

    const load = service.load();
    expect(service.loading()).toBeTrue();

    fail(new Error('bridge gone'));
    await load;

    expect(service.loading()).toBeFalse();
    expect(service.isOpen()).toBeFalse();
  });

  it('shows nothing on a normal start', async () => {
    setShell({ getPostUpdateChangelog: () => Promise.resolve(null) });
    const service = createService();

    await service.load();

    expect(service.isOpen()).toBeFalse();
  });

  it('shows nothing without a shell bridge', async () => {
    const service = createService();

    await service.load();

    expect(service.isOpen()).toBeFalse();
  });

  it('tells the shell once the user dismissed it, so it is not shown again', async () => {
    const dismissPostUpdateChangelog = jasmine.createSpy('dismiss').and.resolveTo(undefined);
    setShell({
      getPostUpdateChangelog: () => Promise.resolve({ version: '3.2.0', notes: 'notes', publishedAt: null }),
      dismissPostUpdateChangelog,
    });
    const service = createService();
    await service.load();

    service.dismiss();
    service.dismiss();

    expect(service.isOpen()).toBeFalse();
    expect(dismissPostUpdateChangelog).toHaveBeenCalledTimes(1);
  });
});

import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UpdateModalComponent } from './update-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function makeState(overrides: Partial<ShellUpdateState> = {}): ShellUpdateState {
  return {
    phase: 'available',
    supported: true,
    currentVersion: '3.0.0',
    version: '3.1.0',
    notes: null,
    publishedAt: null,
    channel: 'stable',
    betaInstalled: false,
    installStrategy: 'inApp',
    downloadUrl: 'https://macro-deck.app/download',
    partialCheck: null,
    error: null,
    failure: null,
    progress: null,
    lastCheckedAt: null,
    ...overrides,
  };
}

describe('UpdateModalComponent', () => {
  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function setShell(shell: Record<string, unknown>): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = shell;
  }

  async function createFixture(): Promise<ComponentFixture<UpdateModalComponent>> {
    const fixture = TestBed.createComponent(UpdateModalComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<UpdateModalComponent>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UpdateModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
  });

  it('survives closing and reopening: progress pushed after the modal was destroyed is on screen on first paint, and the subscription is never torn down', async () => {
    let pushCallback!: (state: ShellUpdateState) => void;
    const unlisten = jasmine.createSpy('unlisten');
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'downloading', progress: { downloaded: 0, total: 100, percent: 0 } })),
      onUpdateState: (callback: (state: ShellUpdateState) => void) => {
        pushCallback = callback;
        return Promise.resolve(unlisten);
      },
    });

    const first = await createFixture();
    pushCallback(makeState({ phase: 'downloading', progress: { downloaded: 40, total: 100, percent: 40 } }));
    await settle(first);
    expect(first.nativeElement.textContent).toContain('40%');

    first.destroy();

    pushCallback(makeState({ phase: 'downloading', progress: { downloaded: 70, total: 100, percent: 70 } }));

    const second = TestBed.createComponent(UpdateModalComponent);
    second.detectChanges();

    expect(second.nativeElement.textContent).toContain('70%');
    expect(unlisten).not.toHaveBeenCalled();
  });

  it('shows a determinate bar at 0% - not the indeterminate one a falsy check would produce', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'downloading', progress: { downloaded: 0, total: 100, percent: 0 } }),
      ),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const bar = fixture.nativeElement.querySelector('progress') as HTMLProgressElement;
    expect(bar.value).toBe(0);
    expect(bar.getAttribute('value')).toBe('0');
  });

  it('renders the version heading, current version and release date', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ version: '3.1.0', currentVersion: '3.0.0', publishedAt: '2026-07-21' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Macro Deck 3.1.0');
    expect(text).toContain('You currently have 3.0.0');
    expect(text).toContain(new Date(2026, 6, 21).toLocaleDateString());
  });

  it('shows no "Invalid Date"/NaN text when no publish date is known', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ publishedAt: null })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain('Invalid Date');
    expect(text).not.toContain('NaN');
  });

  it('renders the changelog through real elements: a heading, bold text and a link', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ notes: '## 3.1.0\n\n**Fixed** a bug. See [notes](https://example.com).' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    // The shared changelog renderer maps a level-2 ATX heading to <h4> (level 1 -> h3, level
    // 3 -> h5) - see StoreMarkdownComponent. A literal `h2` element never appears from any
    // level of `##..###` heading. Scope to the changelog itself: the section's own "What's
    // new" title is an h4 too, and it comes first in document order.
    const changelog = fixture.nativeElement.querySelector('.update-modal__changelog') as HTMLElement;
    expect(changelog.querySelector('h4')?.textContent?.trim()).toBe('3.1.0');
    expect(changelog.querySelector('strong')?.textContent?.trim()).toBe('Fixed');
    const link = changelog.querySelector('a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('https://example.com/');
  });

  it('neutralises raw HTML in the notes: no script element, but the literal text survives', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ notes: 'before <script>alert(1)</script> after' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    expect(fixture.nativeElement.querySelector('script')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('<script>alert(1)</script>');
  });

  it('renders GitHub release notes without the generator comment or a second heading, one bordered section per category', async () => {
    const notes = [
      '<!-- Release notes generated using configuration in .github/release.yml at fd59e92 -->',
      '',
      "## What's Changed",
      '### 🛠 Breaking Changes',
      '* Detect GPUs by @a in https://github.com/Macro-Deck-App/Macro-Deck/pull/697',
      '### 🐞 Bug Fixes',
      '* Run a button press by @a in https://github.com/Macro-Deck-App/Macro-Deck/pull/689',
      '* Keep widget border animating by @a in https://github.com/Macro-Deck-App/Macro-Deck/pull/691',
      '',
      '## New Contributors',
      '* @b made their first contribution in https://github.com/Macro-Deck-App/Macro-Deck/pull/700',
      '',
      '',
      '**Full Changelog**: https://github.com/Macro-Deck-App/Macro-Deck/compare/v3.0.0-beta.2...v3.0.0-beta.3',
    ].join('\n');
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ notes })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text.split("What's new").length - 1).toBe(1);
    expect(text).not.toContain("What's Changed");
    expect(text).not.toContain('<!--');
    expect(text).not.toContain('release.yml');

    const changelog = fixture.nativeElement.querySelector('.update-modal__changelog') as HTMLElement;
    const categories = Array.from(changelog.querySelectorAll<HTMLElement>('.update-modal__notes-section--category'));
    expect(categories.map(section => section.querySelector('.md-heading')?.textContent?.trim()))
      .toEqual(['🛠 Breaking Changes', '🐞 Bug Fixes', 'New Contributors']);
    expect(changelog.textContent).toContain('Full Changelog');
    expect(categories.some(section => section.textContent!.includes('Full Changelog'))).toBeFalse();
  });

  it('shows the no-changelog fallback when the notes are only a generator comment', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ notes: '<!-- Release notes generated using configuration in .github/release.yml -->\n' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    expect(fixture.nativeElement.querySelector('.update-modal__no-changelog')).not.toBeNull();
  });

  it('shows the no-changelog fallback for null notes', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ notes: null })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    expect(fixture.nativeElement.textContent).toContain('No release notes were published for this version.');
    expect(fixture.nativeElement.textContent).not.toContain('null');
  });

  it('shows the no-changelog fallback for whitespace-only notes', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ notes: '   \n  ' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    expect(fixture.nativeElement.textContent).toContain('No release notes were published for this version.');
  });

  it('hides Install and offers the download page for an external-download strategy', async () => {
    const openExternal = jasmine.createSpy('openExternal').and.resolveTo(true);
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ installStrategy: 'externalDownload', downloadUrl: 'https://macro-deck.app/download' })),
      onUpdateState: () => Promise.resolve(() => {}),
      openExternal,
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Open download page');
    expect(text).not.toContain('Download & install');

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    buttons.find(b => b.textContent?.includes('Open download page'))?.click();

    expect(openExternal).toHaveBeenCalledWith('https://macro-deck.app/download');
  });

  it('shows the cancel action and the keep-in-background note while downloading', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'downloading', progress: { downloaded: 10, total: 100, percent: 10 } }),
      ),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Cancel download');
    expect(text).toContain('You can close this window');
    expect(text).not.toContain('Download & install');
  });

  it('cancels the download once and returns to available with no progress element', async () => {
    const cancel = jasmine.createSpy('cancelUpdateDownload').and.callFake(() => Promise.resolve(undefined));
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'downloading', progress: { downloaded: 10, total: 100, percent: 10 } }),
      ),
      onUpdateState: () => Promise.resolve(() => {}),
      cancelUpdateDownload: cancel,
    });

    const fixture = await createFixture();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    buttons.find(b => b.textContent?.includes('Cancel download'))?.click();
    await settle(fixture);

    expect(cancel).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
  });

  it('shows the download error, not an up-to-date message, on a failed phase', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'failed', error: 'network unreachable' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('network unreachable');
    expect(text).not.toContain("You're on the latest version");
  });

  it('words a failed check as a failed check, not as a failed download', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'failed', failure: 'check', version: null, error: 'could not check for updates on any feed (stable, beta)' }),
      ),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain("Couldn't check for updates.");
    expect(text).toContain('could not check for updates on any feed (stable, beta)');
    expect(text).not.toContain('The download failed.');
  });

  it('offers no version and no changelog when a check failed without finding one', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'failed', failure: 'check', version: null, currentVersion: '3.0.0-beta.2', error: 'feed unreachable' }),
      ),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('You currently have 3.0.0-beta.2');
    expect(text).not.toContain('Macro Deck 3.0.0-beta.2');
    expect(text).not.toContain('No release notes were published for this version.');
    expect(fixture.nativeElement.querySelector('.update-modal__changelog')).toBeNull();
  });

  it('still words a failed download as a failed download', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(
        makeState({ phase: 'failed', failure: 'install', version: '3.1.0', error: 'connection reset' }),
      ),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('The download failed.');
    expect(text).toContain('connection reset');
    expect(text).toContain('Macro Deck 3.1.0');
  });

  it('disables Install while a download/install is in flight', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'available' })),
      onUpdateState: () => Promise.resolve(() => {}),
      installUpdate: () => new Promise<void>(() => {}),
    });

    const fixture = await createFixture();
    const install = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(b => b.textContent?.includes('Download & install'));
    install?.click();
    await settle(fixture);

    const installAfter = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(b => b.textContent?.includes('Download & install'));
    expect(installAfter?.disabled).toBeTrue();
  });
});

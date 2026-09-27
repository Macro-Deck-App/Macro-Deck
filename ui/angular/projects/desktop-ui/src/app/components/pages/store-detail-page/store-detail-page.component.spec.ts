import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { ActivatedRoute, Navigation, Router } from '@angular/router';

import { AppStrings, StoreCatalogItemBody, StoreExtensionDetailBody, StoreOperationBody, StoreVersionHistoryBody, StoreCategoryBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { ConnectAccountService } from '../../../services/connect-account.service';
import { IconPackService } from '../../../services/icon-pack.service';
import { IntegrationService } from '../../../services/integration.service';
import { PluginRuntimeService } from '../../../services/plugin-runtime.service';
import { ExternalLinkService } from '../../../services/external-link.service';
import { StoreOperationService } from '../../../services/store-operation.service';
import { StoreDetailPageComponent } from './store-detail-page.component';
import { Observable, Subject } from 'rxjs';

function extension(overrides: Partial<StoreExtensionDetailBody> = {}): StoreExtensionDetailBody {
  return {
    kind: 'Plugin',
    id: 'app.example.plugin',
    name: 'Example Plugin',
    latestVersion: '1.1.0',
    installState: 'NotInstalled',
    trust: 'RegistryAuthenticated',
    hasIcon: false,
    screenshots: [],
    downloadSize: 0,
    supportedOperatingSystems: [],
    languages: [],
    history: [],
    ...overrides,
  };
}

function operation(overrides: Partial<StoreOperationBody> = {}): StoreOperationBody {
  return {
    id: 'op-1',
    kind: 'Install',
    extensionKind: 'Plugin',
    packageId: 'app.example.plugin',
    version: '1.1.0',
    displayName: 'Example Plugin',
    state: 'Downloading',
    bytesDownloaded: 10,
    totalBytes: 100,
    etaSeconds: 20,
    startedAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:01Z',
    canRetry: false,
    ...overrides,
  };
}

describe('StoreDetailPageComponent', () => {
  let fixture: ComponentFixture<StoreDetailPageComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let operationsSpy: jasmine.SpyObj<StoreOperationService>;
  let notifications: Map<string, Subject<unknown>>;
  let integrations: ReturnType<typeof signal<{ id: string }[]>>;
  let previousUrl: string | null = null;
  let iconPacks: ReturnType<typeof signal<{ id: string; storePackageId?: string | null }[]>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  // LocalizationService caches the reader's culture in localStorage and restores it on construction,
  // so a spec that ran earlier and switched language leaves this one reading in that language. The
  // language ordering below is stated relative to the reader, so it has to start from a known one
  // rather than from whatever the suite happened to run first.
  beforeEach(() => {
    integrations = signal<{ id: string }[]>([]);
    iconPacks = signal<{ id: string; storePackageId?: string | null }[]>([]);
    for (const key of [
      'md.localization.culture',
      'md.localization.fallbackCulture',
      'md.localization.translations',
      'md.localization.availableCultures',
      'md.localization.followSystem',
    ]) {
      localStorage.removeItem(key);
    }
  });

  let similarItems: StoreCatalogItemBody[] = [];
  let categories: StoreCategoryBody[] = [];

  beforeEach(() => {
    similarItems = [];
    categories = [];
  });

  async function createFixture(
    initialOperation: StoreOperationBody | null,
    extensionOverrides: Partial<StoreExtensionDetailBody> = {},
    reviewsAvailable = false,
    installs: Record<string, number> = {},
  ): Promise<void> {
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getStoreExtension', 'getStoreExtensionIconUrl', 'getStoreScreenshotUrl', 'getStoreStatus', 'onNotification',
      'getStoreRating', 'getStoreReviews', 'getOwnStoreReview', 'getStoreInstalls', 'getStoreRatings',
      'getStoreSimilar', 'getStoreCategories',
    ]);
    api.getStoreSimilar.and.resolveTo({ items: similarItems });
    api.getStoreCategories.and.callFake(() => Promise.resolve({ categories }));
    api.getStoreRatings.and.resolveTo({ available: false, ratings: {} });
    api.getStoreInstalls.and.resolveTo({ available: true, installs });
    api.getStoreRating.and.resolveTo({ available: reviewsAvailable, rating: null, ratingCount: 0, distribution: [] });
    api.getStoreReviews.and.resolveTo({ available: false, items: [], page: 1, pageSize: 20, totalCount: 0, reviewCount: 0 });
    api.getOwnStoreReview.and.resolveTo({ state: 'SignedOut', review: null });
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    api.getStoreStatus.and.resolveTo({
      developerMode: false,
      registry: { hasCatalog: false, sequence: 0, refreshing: false, stale: false },
    });
    api.getStoreExtension.and.resolveTo({ extension: extension(extensionOverrides) });
    api.getStoreExtensionIconUrl.and.callFake((_kind, _id, sha256) => (sha256 ? `icon-${sha256}` : ''));
    api.getStoreScreenshotUrl.and.callFake((_kind, _id, index, sha256) => `shot-${index}-${sha256}`);
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });

    operationsSpy = jasmine.createSpyObj<StoreOperationService>('StoreOperationService', [
      'operationFor', 'install', 'retry', 'uninstall',
    ]);
    operationsSpy.operationFor.and.returnValue(signal(initialOperation));
    operationsSpy.install.and.resolveTo(null);
    operationsSpy.retry.and.resolveTo(null);
    operationsSpy.uninstall.and.resolveTo({ success: true });

    const routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate', 'currentNavigation', 'lastSuccessfulNavigation']);
    routerSpy.navigate.and.resolveTo(true);
    routerSpy.currentNavigation.and.returnValue(previousUrl === null ? null
      : { previousNavigation: { finalUrl: previousUrl } } as unknown as Navigation);
    routerSpy.lastSuccessfulNavigation.and.returnValue(null);

    TestBed.configureTestingModule({
      imports: [StoreDetailPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: StoreOperationService, useValue: operationsSpy },
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
        { provide: IntegrationService, useValue: { integrations, loadIntegrations: async () => undefined } },
        { provide: IconPackService, useValue: { packs: iconPacks, loadPacks: async () => undefined } },
        { provide: ConnectAccountService, useValue: { session: signal(null) } },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'kind' ? 'Plugin' : 'app.example.plugin'),
              },
            },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(StoreDetailPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('loads the icon and every screenshot, thumbnail and enlarged, by the digest of the current image', async () => {
    await createFixture(null, {
      hasIcon: true,
      iconSha256: 'icon-digest',
      screenshots: [{ index: 0, sha256: 'window-digest' }, { index: 1, sha256: 'card-digest' }],
    });
    const host = fixture.nativeElement as HTMLElement;
    const sources = () => Array.from(host.querySelectorAll('img')).map(img => img.getAttribute('src'));

    expect(sources()).toContain('icon-icon-digest');
    expect(sources()).toContain('shot-0-window-digest');
    expect(sources()).toContain('shot-1-card-digest');

    host.querySelector<HTMLButtonElement>('app-store-screenshot-strip button')!.click();
    fixture.detectChanges();

    expect(Array.from(host.querySelectorAll('app-store-screenshot-viewer img')).map(img => img.getAttribute('src')))
      .toEqual(['shot-0-window-digest']);
  });

  it('lists the install count in the details, and lists nothing when none is known', async () => {
    await createFixture(null, {}, false, { 'app.example.plugin': 4200 });
    await fixture.whenStable();
    fixture.detectChanges();
    const rows = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.detail-row'))
      .map(row => row.textContent?.replace(/\s+/gu, ' ').trim() ?? '');

    expect(rows().some(row => row.includes('4,200'))).toBeTrue();

    TestBed.resetTestingModule();
    await createFixture(null);

    expect(rows().some(row => row.includes('Installs'))).toBeFalse();
  });

  it('offers reporting the item only where the Store can take reports for it', async () => {
    await createFixture(null, {}, false);
    expect((fixture.nativeElement as HTMLElement).querySelector('.detail-report')).toBeNull();

    TestBed.resetTestingModule();
    await createFixture(null, {}, true);
    await fixture.whenStable();
    fixture.detectChanges();
    const report = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.detail-report button');
    expect(report?.textContent)
      .toContain(TestBed.inject(LocalizationService).translateKey(AppStrings.Store.Report.EntryAction));

    report!.click();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('app-store-report-dialog')).not.toBeNull();
  });

  it('renders progress instead of an Install button when an operation is already in flight at construction', async () => {
    await createFixture(operation({ state: 'Downloading' }));

    const installButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Install');
    expect(installButton).toBeUndefined();

    const progressBar = fixture.nativeElement.querySelector('[role="progressbar"]');
    expect(progressBar).not.toBeNull();
  });

  it('offers a retry for a failed operation with canRetry, and calls the host on click', async () => {
    await createFixture(operation({ state: 'Failed', canRetry: true, errorMessage: 'Download failed' }));

    const retryButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Retry');
    expect(retryButton).withContext('a Retry control should be offered').not.toBeUndefined();

    (retryButton as HTMLElement).click();
    await fixture.whenStable();

    expect(operationsSpy.retry).toHaveBeenCalledWith('op-1');
  });

  it('does not offer a retry for a failed operation that cannot be retried', async () => {
    await createFixture(operation({ state: 'Failed', canRetry: false }));

    const retryButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Retry');
    expect(retryButton).toBeUndefined();
  });

  it("shows the newest version's notes and version label in the What's new? card, not the older one's", async () => {
    const history: StoreVersionHistoryBody[] = [
      { version: '2.0.0', releasedAt: '2026-06-03T00:00:00Z', changelog: 'Newest release notes' },
      { version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Older release notes' },
    ];
    await createFixture(null, { history });

    const cardText = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(cardText).toContain('Newest release notes');
    expect(cardText).toContain('Version 2.0.0');
    expect(cardText).not.toContain('Older release notes');
  });

  it('hides the version-history trigger when there is a single history entry', async () => {
    await createFixture(null, {
      history: [{ version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Only release notes' }],
    });
    const historyButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim().startsWith('All versions'));
    expect(historyButton).toBeUndefined();
  });

  it('lists the reader\'s own language first, then English, then the rest by name', async () => {
    // The reader is on 'en' here (the test catalog's default), so "own language" and "English" are the
    // same rule; what the assertion pins is that English leads and the remainder is name-ordered.
    await createFixture(null, { languages: ['ja', 'de', 'en', 'nl'] });

    const items = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.language-item'))
      .map(item => item.textContent?.trim());

    // Ordered by the name the reader sees, in the reader's own collation - which is why a non-Latin
    // name lands after the Latin ones rather than at its language tag's alphabetical position.
    expect(items).toEqual(['English', 'Deutsch', 'Nederlands', '日本語']);
  });

  it('shows at most four languages in the sidebar and offers View all for the rest', async () => {
    await createFixture(null, { languages: ['en', 'de', 'fr', 'es', 'it', 'nl'] });

    expect(fixture.nativeElement.querySelectorAll('.language-item').length).toBe(4);

    const viewAll = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'View all');
    expect(viewAll).withContext('six languages should offer a View all trigger').not.toBeUndefined();

    (viewAll as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    // The modal carries the whole list, in the same order the sidebar sliced its first four from.
    expect(fixture.nativeElement.querySelectorAll('.language-entry').length).toBe(6);
  });

  it('offers no View all when the sidebar already shows every language', async () => {
    await createFixture(null, { languages: ['en', 'de', 'fr', 'es'] });

    const viewAll = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'View all');
    expect(viewAll).toBeUndefined();
  });

  describe('AI declaration', () => {
    const text = (key: string, params?: Record<string, unknown>) =>
      TestBed.inject(LocalizationService).translateKey(key, params);
    const sidebarText = () => (fixture.nativeElement.querySelector('.detail-sidebar') as HTMLElement).textContent ?? '';
    const badge = (key: string) => Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.store-badge'))
      .find(element => element.textContent?.trim() === text(key));
    const headerBadge = () => badge(AppStrings.Store.Page.UsesAiBadge);

    it('says a package without a declaration has not declared anything, never that it uses no AI', async () => {
      await createFixture(null, { ai: null });

      expect(sidebarText()).toContain(text(AppStrings.Store.Page.AiNotDeclared));
      expect(sidebarText()).not.toContain(text(AppStrings.Store.Page.AiNone));
      expect(headerBadge()).toBeUndefined();
    });

    it('states that a package declaring every flag false uses no AI', async () => {
      await createFixture(null, {
        ai: { interaction: false, generatedContent: false, generatedAssets: false, services: [] },
      });

      expect(sidebarText()).toContain(text(AppStrings.Store.Page.AiNone));
      expect(headerBadge()).toBeUndefined();
    });

    it('treats a declaration that only names AI services as using AI, never as using none', async () => {
      await createFixture(null, {
        ai: { interaction: false, generatedContent: false, generatedAssets: false, services: ['OpenAI'] },
      });

      expect(sidebarText()).not.toContain(text(AppStrings.Store.Page.AiNone));
      expect(sidebarText()).toContain(text(AppStrings.Store.Page.AiServices, { services: 'OpenAI' }));
      expect(headerBadge()).toBeDefined();
    });

    it('marks an icon pack with AI-created icons as made with AI, not as using AI', async () => {
      await createFixture(null, {
        kind: 'IconPack',
        ai: { interaction: false, generatedContent: false, generatedAssets: true, services: [] },
      });

      const uses = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.ai-item'))
        .map(item => item.textContent?.trim());
      expect(uses).toEqual([text(AppStrings.IconPacks.AiGenerated)]);
      expect(headerBadge()).toBeUndefined();
      expect(badge(AppStrings.Store.Page.MadeWithAiBadge)).toBeDefined();
    });

    it('lists each declared use and the services, and marks the header', async () => {
      await createFixture(null, {
        ai: { interaction: true, generatedContent: false, generatedAssets: true, services: ['OpenAI', 'ElevenLabs'] },
      });

      const uses = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.ai-item'))
        .map(item => item.textContent?.trim());
      expect(uses).toEqual([text(AppStrings.Store.Page.AiInteraction), text(AppStrings.Store.Page.AiGeneratedAssets)]);
      expect(sidebarText()).toContain(text(AppStrings.Store.Page.AiServices, { services: 'OpenAI, ElevenLabs' }));
      expect(headerBadge()).toBeDefined();
    });
  });

  describe('tags and recommendations', () => {
    const recommended = () =>
      fixture.nativeElement.querySelectorAll('[data-testid="store-detail-similar"] shared-store-extension-card').length;
    const settleSimilar = async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    };

    const similarItem = (id: string): StoreCatalogItemBody => ({
      kind: 'Plugin',
      id,
      name: id,
      latestVersion: '1.0.0',
      installState: 'NotInstalled',
      trust: 'RegistryAuthenticated',
      hasIcon: false,
    });

    it('lists the tags and opens the Store filtered by the one picked', async () => {
      await createFixture(null, { tags: ['streaming', 'obs'] });

      const chips = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[data-testid="store-detail-tags"] button'));
      expect(chips.map(chip => chip.textContent?.trim())).toEqual(['streaming', 'obs']);

      chips[1].click();

      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/store'], { queryParams: { tag: 'obs' } });
    });

    it('lists Store categories in their own section by name and keeps the other tags apart', async () => {
      categories = [{ id: 'music', names: { en: 'Music', de: 'Musik' }, count: 1 }];
      await createFixture(null, { tags: ['music', 'playlist'] });
      await settleSimilar();

      const categoryButtons = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[data-testid="store-detail-categories"] button'));
      const tagButtons = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[data-testid="store-detail-tags"] button'));
      expect(categoryButtons.map(button => button.textContent!.trim())).toEqual(['Music']);
      expect(tagButtons.map(button => button.textContent!.trim())).toEqual(['playlist']);

      categoryButtons[0].click();
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/store'], { queryParams: { tag: 'music' } });
    });

    it('lists an item\'s categories in the order the registry gives them', async () => {
      categories = [
        { id: 'streaming', names: { en: 'Streaming' }, count: 1 },
        { id: 'music', names: { en: 'Music' }, count: 1 },
      ];
      await createFixture(null, { tags: ['music', 'streaming'] });
      await settleSimilar();

      const labels = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[data-testid="store-detail-categories"] button'))
        .map(button => button.textContent!.trim());
      expect(labels).toEqual(['Streaming', 'Music']);
    });

    it('shows no categories section when none of the tags is a Store category', async () => {
      categories = [{ id: 'music', names: { en: 'Music' }, count: 1 }];
      await createFixture(null, { tags: ['playlist'] });
      await settleSimilar();

      expect(fixture.nativeElement.querySelector('[data-testid="store-detail-categories"]')).toBeNull();
    });

    it('shows no tag section when the registry declares none', async () => {
      await createFixture(null, { tags: [] });

      expect(fixture.nativeElement.querySelector('[data-testid="store-detail-tags"]')).toBeNull();
    });

    it('recommends the packages the host ranks as similar', async () => {
      similarItems = [similarItem('app.example.first'), similarItem('app.example.second')];
      await createFixture(null);
      await settleSimilar();

      expect(recommended()).toBe(2);
      expect(api.getStoreSimilar).toHaveBeenCalledWith('Plugin', 'app.example.plugin');
    });

    it('hides the recommendations when there are none or the host reports an error', async () => {
      await createFixture(null);
      await settleSimilar();
      const empty = recommended();

      api.getStoreSimilar.and.resolveTo({ items: [similarItem('x')], error: { code: 'registry_unavailable', message: '' } });
      push('StoreCatalogChangedEvent', {});
      await settleSimilar();

      expect(empty).toBe(0);
      expect(recommended()).toBe(0);
    });

    it('refreshes the recommendations after an install changes the catalog', async () => {
      similarItems = [similarItem('app.example.first')];
      await createFixture(null);
      await settleSimilar();
      const before = recommended();

      api.getStoreSimilar.and.resolveTo({ items: [] });
      push('StoreCatalogChangedEvent', {});
      await settleSimilar();

      expect(before).toBe(1);
      expect(recommended()).toBe(0);
    });
  });

  it('shows no language section at all when the registry declares none', async () => {
    await createFixture(null, { languages: [] });

    expect(fixture.nativeElement.querySelector('.languages-group')).toBeNull();
  });

  function renderedLinks(): { text: string | undefined; href: string | null }[] {
    return Array.from<HTMLAnchorElement>(fixture.nativeElement.querySelectorAll('.links-group a'))
      .map(link => ({ text: link.textContent?.trim(), href: link.getAttribute('href') }));
  }

  it('shows the repository alone exactly as before when a package declares no additional links', async () => {
    await createFixture(null, { repository: 'https://github.com/example/plugin' });

    expect(renderedLinks()).toEqual([{ text: 'Repository', href: 'https://github.com/example/plugin' }]);
  });

  it('lists additional links after the repository, with Macro Deck labels for standard types and the author label for custom ones', async () => {
    await createFixture(null, {
      repository: 'https://github.com/example/plugin',
      additionalLinks: [
        { type: 'issues', url: 'https://github.com/example/plugin/issues' },
        { type: 'custom', label: 'Setup Guide', url: 'https://example.com/setup' },
        { type: 'privacy', url: 'https://example.com/privacy' },
      ],
    });

    expect(renderedLinks()).toEqual([
      { text: 'Repository', href: 'https://github.com/example/plugin' },
      { text: 'Report an issue', href: 'https://github.com/example/plugin/issues' },
      { text: 'Setup Guide', href: 'https://example.com/setup' },
      { text: 'Privacy policy', href: 'https://example.com/privacy' },
    ]);
    for (const link of Array.from<HTMLAnchorElement>(fixture.nativeElement.querySelectorAll('.links-group a'))) {
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    }
  });

  it('shows the links section for additional links even without a repository, and skips links it cannot label', async () => {
    await createFixture(null, {
      additionalLinks: [
        { type: 'roadmap', url: 'https://example.com/roadmap' },
        { type: 'custom', url: 'https://example.com/unlabelled' },
        { type: 'documentation', url: 'https://docs.example.com' },
      ],
    });

    expect(renderedLinks()).toEqual([{ text: 'Documentation', href: 'https://docs.example.com' }]);
  });

  it('puts the homepage first, ahead of the repository', async () => {
    await createFixture(null, {
      homepage: 'https://example.com/plugin',
      repository: 'https://github.com/example/plugin',
      additionalLinks: [{ type: 'issues', url: 'https://github.com/example/plugin/issues' }],
    });

    expect(renderedLinks()).toEqual([
      { text: 'Homepage', href: 'https://example.com/plugin' },
      { text: 'Repository', href: 'https://github.com/example/plugin' },
      { text: 'Report an issue', href: 'https://github.com/example/plugin/issues' },
    ]);
  });

  it('lists a homepage that is the repository only once', async () => {
    await createFixture(null, {
      homepage: 'https://github.com/example/plugin',
      repository: 'https://github.com/example/plugin',
    });

    expect(renderedLinks()).toEqual([{ text: 'Repository', href: 'https://github.com/example/plugin' }]);
  });

  it('never links a homepage that is not https', async () => {
    await createFixture(null, { homepage: 'http://example.com/plugin' });

    expect(fixture.nativeElement.querySelector('.links-group')).toBeNull();
  });

  it('shows no links section when there is neither a repository nor an additional link', async () => {
    await createFixture(null, { additionalLinks: [] });

    expect(fixture.nativeElement.querySelector('.links-group')).toBeNull();
  });

  it('shows the version-history trigger when there are two history entries', async () => {
    await createFixture(null, {
      history: [
        { version: '2.0.0', releasedAt: '2026-06-03T00:00:00Z', changelog: 'Newest release notes' },
        { version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Older release notes' },
      ],
    });
    const historyButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'All versions (2)');
    expect(historyButton).withContext('two history entries should offer a Version history trigger').not.toBeUndefined();
  });

  it('reloads the extension when the catalog changes, so a stale Installed state does not survive an uninstall', async () => {
    await createFixture(null, { installState: 'Installed', installedVersion: '1.1.0' });
    expect(fixture.nativeElement.textContent).toContain('Installed');

    api.getStoreExtension.and.resolveTo({ extension: extension({ installState: 'NotInstalled' }) });
    push('StoreCatalogChangedEvent', {});
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.getStoreExtension).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).not.toContain('Installed');
  });

  it('wires the Uninstall control through to the operations service with the exact kind and id, and surfaces a failure with its localized reason', async () => {
    jasmine.clock().install();
    try {
      await createFixture(null, { kind: 'Plugin', id: 'app.example.plugin', installState: 'Installed', installedVersion: '1.1.0' });
      operationsSpy.uninstall.and.resolveTo({ success: false, error: { code: 'DependencyInUse', message: 'raw host message' } });

      const localization = TestBed.inject(LocalizationService);
      const uninstallButtons = (): HTMLElement[] => Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
        .filter(btn => btn.textContent?.trim() === localization.translateKey(AppStrings.Store.Uninstall));

      uninstallButtons()[0].click();
      fixture.detectChanges();
      await fixture.whenStable();

      // The confirmation modal's own confirm button carries the same label; it is the last one in the
      // DOM. Confirming plays a 150ms close animation before the confirm output actually fires (see
      // ModalComponent.dismiss), so the clock has to be advanced past it.
      uninstallButtons()[uninstallButtons().length - 1].click();
      jasmine.clock().tick(150);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(operationsSpy.uninstall).toHaveBeenCalledWith('Plugin', 'app.example.plugin');

      const toasts = TestBed.inject(ToastService).toasts();
      expect(toasts.length).toBe(1);
      expect(toasts[0].message).toBe(localization.translateKey(AppStrings.Store.UninstallFailed));
      expect(toasts[0].detail).toBe(localization.translateKey(AppStrings.Store.UninstallDependencyInUse));
      expect(toasts[0].detail).not.toBe('raw host message');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('calls the operations service zero times when the uninstall confirmation is cancelled', async () => {
    jasmine.clock().install();
    try {
      await createFixture(null, { kind: 'Plugin', id: 'app.example.plugin', installState: 'Installed', installedVersion: '1.1.0' });

      const localization = TestBed.inject(LocalizationService);
      const findButton = (text: string): HTMLElement | undefined => Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
        .find(btn => btn.textContent?.trim() === text);

      findButton(localization.translateKey(AppStrings.Store.Uninstall))!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();

      findButton(localization.translateKey('macrodeck:Common.Cancel'))!.click();
      jasmine.clock().tick(150);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(operationsSpy.uninstall).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the icon-pack-specific confirmation copy, distinct from the plugin one, resolved through the localization service', async () => {
    await createFixture(null, { kind: 'IconPack', name: 'Neon Icons', installState: 'Installed', installedVersion: '1.0.0' });

    const localization = TestBed.inject(LocalizationService);
    const uninstallButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(btn => btn.textContent?.trim() === localization.translateKey(AppStrings.Store.Uninstall));
    uninstallButton!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const expected = localization.translateKey(AppStrings.Store.UninstallMessageIconPack, { name: 'Neon Icons' });
    const pluginWording = localization.translateKey(AppStrings.Store.UninstallMessagePlugin, { name: 'Neon Icons' });
    expect(fixture.nativeElement.textContent).toContain(expected);
    expect(fixture.nativeElement.textContent).not.toContain(pluginWording);
  });

  it('renders the confirmation modal outside the install button, not inside it', async () => {
    await createFixture(null, { installState: 'Installed', installedVersion: '1.1.0' });

    const localization = TestBed.inject(LocalizationService);
    const uninstallButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(btn => btn.textContent?.trim() === localization.translateKey(AppStrings.Store.Uninstall));
    uninstallButton!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const modal = fixture.nativeElement.querySelector('shared-confirmation-modal');
    expect(modal).not.toBeNull();

    const installButtons = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-store-install-button'));
    expect(installButtons.length).toBeGreaterThan(0);
    for (const installButtonEl of installButtons) {
      expect(installButtonEl.contains(modal)).toBeFalse();
    }
  });

  describe('version selection', () => {
    const history: StoreVersionHistoryBody[] = [
      { version: '2.0.0', releasedAt: '2026-06-03T00:00:00Z', installable: true },
      { version: '1.5.0', releasedAt: '2026-03-01T00:00:00Z', installable: true },
      { version: '1.0.0', releasedAt: '2026-01-01T00:00:00Z', installable: false, unavailableReason: 'Unavailable' },
    ];

    function selectVersion(version: string): void {
      (fixture.componentInstance as unknown as { onVersionChange(value: string): void }).onVersionChange(version);
      fixture.detectChanges();
    }

    function clickButton(label: string): void {
      Array.from<HTMLButtonElement>((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
        .find(button => button.textContent?.trim() === label)!.click();
      fixture.detectChanges();
    }

    function translate(key: string, params?: Record<string, unknown>): string {
      return TestBed.inject(LocalizationService).translateKey(key, params);
    }

    it('installs the latest release as an ordinary install when nothing else is selected', async () => {
      await createFixture(null, { latestVersion: '2.0.0', history });

      clickButton(translate(AppStrings.Store.Install));

      expect(operationsSpy.install).toHaveBeenCalledWith('Plugin', 'app.example.plugin', undefined);
    });

    it('installs exactly the older version the reader selected', async () => {
      await createFixture(null, { latestVersion: '2.0.0', history });

      selectVersion('1.5.0');
      clickButton(translate(AppStrings.Store.InstallVersion, { version: '1.5.0' }));

      expect(operationsSpy.install).toHaveBeenCalledWith('Plugin', 'app.example.plugin', '1.5.0');
    });

    it('asks before replacing the installed version with an older one', async () => {
      await createFixture(null, { latestVersion: '2.0.0', installedVersion: '2.0.0', installState: 'Installed', history });

      selectVersion('1.5.0');
      clickButton(translate(AppStrings.Store.DowngradeTo, { version: '1.5.0' }));
      expect(operationsSpy.install).not.toHaveBeenCalled();

      const dialogButtons = Array.from<HTMLButtonElement>(document.body.querySelectorAll('shared-confirmation-modal button'));
      jasmine.clock().install();
      try {
        // ModalComponent plays a 150ms close animation before the confirm output fires.
        dialogButtons.find(button => button.textContent?.trim() === translate(AppStrings.Store.DowngradeTo, { version: '1.5.0' }))!.click();
        jasmine.clock().tick(150);
        fixture.detectChanges();
        await fixture.whenStable();
      } finally {
        jasmine.clock().uninstall();
      }

      expect(operationsSpy.install).toHaveBeenCalledWith('Plugin', 'app.example.plugin', '1.5.0');
    });

    it('selects the Store\'s latest release by default even when the history lists a higher version', async () => {
      await createFixture(null, {
        latestVersion: '1.5.0',
        history: [
          { version: '2.0.0', releasedAt: '2026-06-03T00:00:00Z', installable: true },
          { version: '1.5.0', releasedAt: '2026-03-01T00:00:00Z', installable: true },
        ],
      });

      clickButton(translate(AppStrings.Store.Install));

      expect(operationsSpy.install).toHaveBeenCalledWith('Plugin', 'app.example.plugin', undefined);
    });

    it('keeps a version that cannot be installed in the list but out of reach', async () => {
      await createFixture(null, { latestVersion: '2.0.0', history });
      const component = fixture.componentInstance as unknown as { versionOptions(): { value: string; disabled?: boolean }[] };

      expect(component.versionOptions().map(option => [option.value, !!option.disabled]))
        .toEqual([['2.0.0', false], ['1.5.0', false], ['1.0.0', true]]);
    });

    it('offers only the latest release when the host cannot say which older ones are installable', async () => {
      await createFixture(null, {
        latestVersion: '2.0.0',
        history: history.map(entry => ({ version: entry.version, releasedAt: entry.releasedAt })),
      });
      const component = fixture.componentInstance as unknown as { versionOptions(): { value: string; disabled?: boolean }[] };

      expect(component.versionOptions().filter(option => !option.disabled).map(option => option.value)).toEqual(['2.0.0']);
    });

    it('warns about an installed withdrawn version and offers going back to the latest only after confirming', async () => {
      await createFixture(null, {
        latestVersion: '1.2.0',
        installedVersion: '1.3.0',
        installState: 'Installed',
        installedVersionWithdrawal: { reason: 'Compromised signing key', replacement: 'com.acme.safe' },
        history: [
          { version: '1.3.0', installable: false, unavailableReason: 'Withdrawn' },
          { version: '1.2.0', installable: true },
        ],
      });

      const notice = (fixture.nativeElement as HTMLElement).querySelector('.withdrawal-notice')?.textContent ?? '';
      clickButton(translate(AppStrings.Store.DowngradeTo, { version: '1.2.0' }));

      expect(notice).toContain(translate(AppStrings.Store.Withdrawal.InstalledHeading, { version: '1.3.0' }));
      expect(notice).toContain('Compromised signing key');
      expect(notice).toContain('com.acme.safe');
      expect(operationsSpy.install).not.toHaveBeenCalled();
      expect(document.body.querySelector('shared-confirmation-modal')).not.toBeNull();
    });

    it('says a withdrawn version was withdrawn rather than merely unavailable', async () => {
      await createFixture(null, {
        latestVersion: '2.0.0',
        history: [
          { version: '2.0.0', installable: true },
          { version: '1.0.0', installable: false, unavailableReason: 'Withdrawn' },
        ],
      });
      const component = fixture.componentInstance as unknown as {
        versionOptions(): { value: string; badge?: string }[];
        selectedUnavailableReason(): string | null;
      };

      selectVersion('1.0.0');

      expect(component.versionOptions().find(option => option.value === '1.0.0')?.badge)
        .toContain(translate(AppStrings.Store.VersionWithdrawn));
      expect(component.selectedUnavailableReason()).toBe(translate(AppStrings.Store.Page.VersionWithdrawnReason));
    });

    it('keeps Uninstall and no reviews for an installed package the registry removed from the Store', async () => {
      await createFixture(null, {
        latestVersion: '2.0.0',
        installedVersion: '1.0.0',
        installState: 'Installed',
        withdrawal: { reason: 'Malware' },
        history: [
          { version: '2.0.0', installable: false, unavailableReason: 'Withdrawn' },
          { version: '1.0.0', installable: false, unavailableReason: 'Withdrawn' },
        ],
      }, true);
      const host = fixture.nativeElement as HTMLElement;
      const labels = Array.from(host.querySelectorAll('button')).map(button => button.textContent?.trim());

      expect(host.querySelector('.withdrawal-notice')?.textContent).toContain(translate(AppStrings.Store.Withdrawal.PackageHeading));
      expect(labels).toContain(translate(AppStrings.Store.Uninstall));
      expect(host.querySelector('app-store-reviews-section')).toBeNull();
    });

    it('opens detail links through the system browser rather than navigating the app', async () => {
      await createFixture(null, { repository: 'https://github.com/example/plugin' });
      const externalLinks = TestBed.inject(ExternalLinkService);
      spyOn(externalLinks, 'open');

      (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('.links-group a')!.click();

      expect(externalLinks.open).toHaveBeenCalledWith('https://github.com/example/plugin');
    });

    it('drops a link that is not https', async () => {
      await createFixture(null, { repository: 'javascript:alert(1)' });

      expect((fixture.nativeElement as HTMLElement).querySelector('.links-group')).toBeNull();
    });
  });

  describe('long release notes', () => {
    it('shows the first lines of long release notes until the reader expands them', async () => {
      const changelog = Array.from({ length: 30 }, (_, index) => `- Change ${index + 1}`).join('\n');
      await createFixture(null, { history: [{ version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog }] });
      const host = fixture.nativeElement as HTMLElement;

      expect(host.querySelector('.whats-new-notes')!.classList).toContain('collapsed');
      (host.querySelector('.whats-new-toggle') as HTMLElement).click();
      fixture.detectChanges();
      expect(host.querySelector('.whats-new-notes')!.classList).not.toContain('collapsed');
    });

    it('shows short release notes in full without a toggle', async () => {
      await createFixture(null, { history: [{ version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Small fix' }] });
      const host = fixture.nativeElement as HTMLElement;

      expect(host.querySelector('.whats-new-notes')!.classList).not.toContain('collapsed');
      expect(host.querySelector('.whats-new-toggle')).toBeNull();
    });
  });

  describe('platforms', () => {
    function platforms(): { name: string; available: boolean; label: string | null }[] {
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.platform')).map(platform => ({
        name: platform.querySelector('.platform-name')?.textContent?.trim() ?? '',
        available: !platform.classList.contains('unavailable'),
        label: platform.getAttribute('aria-label'),
      }));
    }

    it('marks exactly the platforms a package supports', async () => {
      await createFixture(null, { supportedOperatingSystems: ['Windows'] });

      expect(platforms().map(platform => [platform.name, platform.available]))
        .toEqual([['Windows', true], ['macOS', false], ['Linux', false]]);
      expect(platforms()[1].label).toBe('macOS: not supported');
    });

    it('treats a package that names no platform as running everywhere', async () => {
      await createFixture(null, { supportedOperatingSystems: [] });

      expect(platforms().every(platform => platform.available)).toBeTrue();
    });
  });

  describe('plugin settings', () => {
    function settingsButton(): HTMLElement | undefined {
      return Array.from<HTMLElement>((fixture.nativeElement as HTMLElement).querySelectorAll('app-store-detail-header button'))
        .find(button => button.textContent?.trim() === TestBed.inject(LocalizationService).translateKey(AppStrings.Store.Page.OpenSettingsAction));
    }

    it('takes an installed plugin straight to its settings', async () => {
      integrations.set([{ id: 'app.example.plugin' }]);
      await createFixture(null, { installState: 'Installed', installedVersion: '1.1.0' });

      settingsButton()!.click();

      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/integrations', 'app.example.plugin']);
    });

    it('takes an installed icon pack straight to it in the Library', async () => {
      iconPacks.set([{ id: 'pack-7', storePackageId: 'app.example.plugin' }]);
      await createFixture(null, { kind: 'IconPack', installState: 'Installed', installedVersion: '1.1.0' });

      const button = Array.from<HTMLElement>((fixture.nativeElement as HTMLElement).querySelectorAll('app-store-detail-header button'))
        .find(b => b.textContent?.trim() === TestBed.inject(LocalizationService).translateKey(AppStrings.Store.Page.OpenInLibraryAction))!;
      button.click();

      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/library/icon-packs'], { queryParams: { pack: 'pack-7' } });
    });

    it('offers no settings for a plugin that is not installed', async () => {
      integrations.set([{ id: 'app.example.plugin' }]);
      await createFixture(null, { installState: 'NotInstalled' });

      expect(settingsButton()).toBeUndefined();
    });
  });

  describe('going back', () => {
    afterEach(() => {
      previousUrl = null;
    });

    it('returns to the list the entry was opened from, exactly as it was', async () => {
      previousUrl = '/store?kind=Plugin&sort=name';
      await createFixture(null);
      const location = TestBed.inject(Location);
      spyOn(location, 'back');

      (fixture.componentInstance as unknown as { goBack(): void }).goBack();

      expect(location.back).toHaveBeenCalled();
    });

    it('returns to the entry a recommendation was opened from', async () => {
      previousUrl = '/store/IconPack/com.example.icons';
      await createFixture(null);
      const location = TestBed.inject(Location);
      spyOn(location, 'back');

      (fixture.componentInstance as unknown as { goBack(): void }).goBack();

      expect(location.back).toHaveBeenCalled();
      expect(TestBed.inject(Router).navigate).not.toHaveBeenCalledWith(['/store']);
    });

    it('opens Discover when the entry was reached from somewhere else, such as an integration', async () => {
      previousUrl = '/integrations/app.example.plugin';
      await createFixture(null);
      const location = TestBed.inject(Location);
      spyOn(location, 'back');

      (fixture.componentInstance as unknown as { goBack(): void }).goBack();

      expect(location.back).not.toHaveBeenCalled();
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/store']);
    });
  });
});

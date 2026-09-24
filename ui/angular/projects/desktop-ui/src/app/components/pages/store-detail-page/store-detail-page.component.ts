import { DatePipe, Location } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { AppStrings, StoreCatalogItemBody, StoreExtensionDetailBody, StoreExtensionKind, StoreVersionHistoryBody } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, LocalizationKey, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { DetailPageComponent } from '../../detail-page/detail-page.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { SelectOption } from '../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { StoreFooterComponent } from '../../store/store-footer.component';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { StoreReportDialogComponent, StoreReportTarget } from '../../store/store-report-dialog.component';
import { StoreReviewsSectionComponent } from '../../store/store-reviews-section.component';
import { cultureDisplayName, sortCulturesForReader } from '../../../localization/culture-display.util';
import { ConnectAccountService } from '../../../services/connect-account.service';
import { SettingsModalService } from '../../../services/settings-modal.service';
import { STORE_DETAIL_KINDS, isStoreDetailUrl, isStoreListUrl } from '../../../services/store-browse-state.service';
import { StoreCategoryService, storeCategoryName } from '../../../services/store-category.service';
import { StoreOperationService } from '../../../services/store-operation.service';
import { StoreSectionComponent, StoreUnsignedInstallRequest } from '../../store/store-section.component';
import { StoreRatingsService } from '../../../services/store-ratings.service';
import { ExternalLinkService } from '../../../services/external-link.service';
import { IconPackService } from '../../../services/icon-pack.service';
import { IntegrationService } from '../../../services/integration.service';
import { UpdateModalService } from '../../../services/update-modal.service';
import { UpdateService } from '../../../services/update.service';
import { formatBytes } from '../../../util/format-bytes';
import { compareVersions, sameVersion } from '../../../util/semver-compare';
import { formatStoreCount } from '../../../util/store-rating-format';
import { storeUninstallErrorKey, storeUninstallMessageKey } from '../../../util/store-operation-display';
import { StoreManageAction } from '../../store/store-install-button.component';
import { StoreDetailHeaderComponent } from './store-detail-header.component';
import { StoreLanguagesModalComponent } from './store-languages-modal.component';
import { StoreScreenshotStripComponent } from './store-screenshot-strip.component';
import { StoreVersionHistoryModalComponent } from './store-version-history-modal.component';

const KNOWN_KINDS = STORE_DETAIL_KINDS;

const SIDEBAR_LANGUAGE_LIMIT = 4;

const PLATFORMS: readonly { name: string; icon: string }[] = [
  { name: 'Windows', icon: 'windows' },
  { name: 'macOS', icon: 'apple' },
  { name: 'Linux', icon: 'linux' },
];

const LONG_CHANGELOG_CHARACTERS = 600;
const LONG_CHANGELOG_LINES = 10;

const STANDARD_LINK_LABELS: Readonly<Record<string, LocalizationKey>> = {
  documentation: AppStrings.Store.Page.Link.Documentation,
  wiki: AppStrings.Store.Page.Link.Wiki,
  issues: AppStrings.Store.Page.Link.Issues,
  support: AppStrings.Store.Page.Link.Support,
  community: AppStrings.Store.Page.Link.Community,
  donate: AppStrings.Store.Page.Link.Donate,
  privacy: AppStrings.Store.Page.Link.Privacy,
  terms: AppStrings.Store.Page.Link.Terms,
  changelog: AppStrings.Store.Page.Link.Changelog,
  license: AppStrings.Store.Page.Link.License,
};

interface StoreAiDeclarationView {
  state: 'undeclared' | 'none' | 'declared';
  uses: LocalizationKey[];
  services: string;
}

interface StoreDetailLink {
  url: string;
  labelKey: LocalizationKey | null;
  label: string | null;
}

@Component({
  selector: 'app-store-detail-page',
  standalone: true,
  imports: [
    DatePipe,
    ButtonComponent,
    ConfirmationModalComponent,
    DetailPageComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    StoreDetailHeaderComponent,
    StoreFooterComponent,
    StoreLanguagesModalComponent,
    StoreScreenshotStripComponent,
    StoreSectionComponent,
    StoreMarkdownComponent,
    StoreReportDialogComponent,
    StoreReviewsSectionComponent,
    StoreVersionHistoryModalComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-detail-page.component.html',
  styleUrls: ['./store-detail-page.component.scss'],
})
export class StoreDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);
  private readonly categories = inject(StoreCategoryService);
  private readonly account = inject(ConnectAccountService);
  private readonly settingsModal = inject(SettingsModalService);
  protected readonly operations = inject(StoreOperationService);
  private readonly ratings = inject(StoreRatingsService);
  private readonly reviewsSection = viewChild(StoreReviewsSectionComponent);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly externalLinks = inject(ExternalLinkService);
  private readonly integrations = inject(IntegrationService);
  private readonly iconPacks = inject(IconPackService);
  private readonly location = inject(Location);
  private openedInStore = false;
  private readonly appUpdates = inject(UpdateService);
  private readonly updateModal = inject(UpdateModalService);

  protected readonly updatesAvailable = this.appUpdates.hasBridge;

  protected readonly kind = signal<StoreExtensionKind | null>(null);
  protected readonly extensionId = signal('');

  protected readonly extension = signal<StoreExtensionDetailBody | null>(null);
  protected readonly isLoading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly operation = computed(() => {
    const kind = this.kind();
    const id = this.extensionId();
    return kind ? this.operations.operationFor(kind, id)() : null;
  });

  protected readonly formatBytes = formatBytes;

  protected readonly installCount = computed(() => {
    const count = this.ratings.installs().get(this.extensionId());
    return count && count > 0 ? formatStoreCount(count, this.localization.culture()) : null;
  });

  protected readonly uninstallConfirmOpen = signal(false);

  protected readonly uninstallHeading = computed(() => this.localization.translateKey(AppStrings.Store.UninstallHeading));

  protected readonly uninstallMessage = computed(() => {
    const extension = this.extension();
    if (!extension) {
      return '';
    }
    return this.localization.translateKey(storeUninstallMessageKey(extension.kind), { name: extension.name });
  });

  protected readonly latestChangelogEntry = computed<StoreVersionHistoryBody | null>(() => {
    const extension = this.extension();
    if (!extension) {
      return null;
    }
    const [latest] = extension.history;
    if (latest) {
      return latest;
    }
    return extension.changelog
      ? { version: extension.latestVersion, releasedAt: extension.updatedAt, changelog: extension.changelog }
      : null;
  });

  private readonly installedIconPackId = computed(() => {
    const extension = this.extension();
    return extension?.kind === 'IconPack'
      ? this.iconPacks.packs().find(pack => pack.storePackageId === extension.id)?.id ?? null
      : null;
  });

  protected readonly manageAction = computed<StoreManageAction | null>(() => {
    const extension = this.extension();
    if (!extension || (extension.installState !== 'Installed' && extension.installState !== 'UpdateAvailable')) {
      return null;
    }
    if (extension.kind === 'Plugin' && this.integrations.integrations().some(integration => integration.id === extension.id)) {
      return 'settings';
    }
    return this.installedIconPackId() ? 'library' : null;
  });

  protected readonly changelogExpanded = signal(false);

  protected readonly changelogIsLong = computed(() => {
    const changelog = this.latestChangelogEntry()?.changelog ?? '';
    return changelog.length > LONG_CHANGELOG_CHARACTERS || changelog.split('\n').length > LONG_CHANGELOG_LINES;
  });

  protected readonly hasVersionHistory = computed(() => (this.extension()?.history.length ?? 0) > 1);

  protected readonly selectedVersion = signal<string | null>(null);

  protected readonly defaultVersion = computed(() => {
    const extension = this.extension();
    if (!extension) {
      return null;
    }
    const latest = extension.history.find(entry => sameVersion(entry.version, extension.latestVersion));
    if (!latest || this.isInstallable(latest)) {
      return extension.latestVersion;
    }
    return extension.history.find(entry => this.isInstallable(entry))?.version ?? extension.latestVersion;
  });

  protected readonly effectiveVersion = computed(() => this.selectedVersion() ?? this.defaultVersion());

  private readonly selectedEntry = computed(() => {
    const version = this.effectiveVersion();
    return this.extension()?.history.find(entry => sameVersion(entry.version, version)) ?? null;
  });

  protected readonly selectedInstallable = computed(() => {
    const entry = this.selectedEntry();
    return entry ? this.isInstallable(entry) : true;
  });

  protected readonly selectedUnavailableReason = computed(() => {
    const entry = this.selectedEntry();
    return entry && !this.isInstallable(entry) ? this.unavailableReason(entry) : null;
  });

  protected readonly otherVersionsAvailable = computed(() => {
    const version = this.effectiveVersion();
    return (this.extension()?.history ?? [])
      .some(entry => this.isInstallable(entry) && !sameVersion(entry.version, version));
  });

  protected readonly versionOptions = computed<SelectOption[]>(() => {
    const extension = this.extension();
    if (!extension) {
      return [];
    }
    return extension.history.map(entry => {
      const installable = this.isInstallable(entry);
      const badges = [
        sameVersion(entry.version, extension.latestVersion) ? this.localization.translateKey(AppStrings.Store.Page.VersionLatestBadge) : null,
        sameVersion(entry.version, extension.installedVersion) ? this.localization.translateKey(AppStrings.Store.Installed) : null,
        installable ? null : this.localization.translateKey(AppStrings.Store.VersionUnavailable),
      ].filter((badge): badge is string => badge !== null);
      return { value: entry.version, label: entry.version, disabled: !installable, badge: badges.join(' · ') || undefined };
    });
  });

  protected readonly selectedIsLatest = computed(() => {
    const extension = this.extension();
    return !extension || sameVersion(this.effectiveVersion(), extension.latestVersion);
  });

  protected readonly versionHints = computed(() => {
    if (this.selectedIsLatest() || !this.selectedInstallable()) {
      return this.selectedUnavailableReason() ? [this.selectedUnavailableReason()!] : [];
    }
    return [
      this.localization.translateKey(AppStrings.Store.Page.OlderVersionHoldHint),
      this.localization.translateKey(AppStrings.Store.Page.CompatibilityCheckedHint),
    ];
  });

  protected readonly downgradeConfirmOpen = signal(false);

  protected readonly downgradeMessage = computed(() => {
    const extension = this.extension();
    if (!extension) {
      return '';
    }
    return this.localization.translateKey(AppStrings.Store.Page.DowngradeConfirmMessage, {
      name: extension.name,
      installed: extension.installedVersion ?? '',
      version: this.effectiveVersion() ?? '',
    });
  });

  protected readonly historyModalOpen = signal(false);
  private historyTriggerElement: HTMLElement | null = null;

  protected readonly languages = computed(() =>
    sortCulturesForReader(this.extension()?.languages ?? [], this.localization.culture()));

  protected readonly aiDeclaration = computed<StoreAiDeclarationView>(() => {
    const ai = this.extension()?.ai;
    if (!ai) {
      return { state: 'undeclared', uses: [], services: '' };
    }
    const uses: LocalizationKey[] = [];
    if (ai.interaction) {
      uses.push(AppStrings.Store.Page.AiInteraction);
    }
    if (ai.generatedContent) {
      uses.push(AppStrings.Store.Page.AiGeneratedContent);
    }
    if (ai.generatedAssets) {
      uses.push(this.extension()?.kind === 'IconPack'
        ? AppStrings.IconPacks.AiGenerated
        : AppStrings.Store.Page.AiGeneratedAssets);
    }
    const services = ai.services ?? [];
    return {
      state: uses.length > 0 || services.length > 0 ? 'declared' : 'none',
      uses,
      services: services.join(', '),
    };
  });

  protected readonly packageCategories = computed(() => {
    const culture = this.localization.culture();
    const tags = this.extension()?.tags ?? [];
    return this.categories.categories()
      .filter(category => tags.includes(category.id))
      .map(category => ({ id: category.id, label: storeCategoryName(category, culture) }));
  });

  protected readonly tags = computed(() => this.categories.settled()
    ? (this.extension()?.tags ?? []).filter(id => this.categories.find(id) === null)
    : []);

  protected readonly similar = signal<StoreCatalogItemBody[]>([]);
  private similarGeneration = 0;

  protected readonly visibleLanguages = computed(() => this.languages().slice(0, SIDEBAR_LANGUAGE_LIMIT));

  protected readonly hasMoreLanguages = computed(() => this.languages().length > SIDEBAR_LANGUAGE_LIMIT);

  protected readonly links = computed<StoreDetailLink[]>(() => {
    const extension = this.extension();
    if (!extension) {
      return [];
    }
    const links: StoreDetailLink[] = [];
    if (extension.homepage && isHttps(extension.homepage) && extension.homepage !== extension.repository) {
      links.push({ url: extension.homepage, labelKey: AppStrings.Store.Page.HomepageLink, label: null });
    }
    if (extension.repository && isHttps(extension.repository)) {
      links.push({ url: extension.repository, labelKey: AppStrings.Store.Page.RepositoryLink, label: null });
    }
    for (const link of extension.additionalLinks ?? []) {
      if (!isHttps(link.url)) {
        continue;
      }
      const labelKey = Object.hasOwn(STANDARD_LINK_LABELS, link.type) ? STANDARD_LINK_LABELS[link.type] : null;
      if (labelKey) {
        links.push({ url: link.url, labelKey, label: null });
      } else if (link.type === 'custom' && link.label) {
        links.push({ url: link.url, labelKey: null, label: link.label });
      }
    }
    return links;
  });

  protected readonly languagesModalOpen = signal(false);
  private languagesTriggerElement: HTMLElement | null = null;

  protected readonly cultureDisplayName = cultureDisplayName;

  protected readonly reportAvailable = computed(() => this.reviewsSection()?.available() ?? false);
  protected readonly reportTarget = signal<StoreReportTarget | null>(null);
  private readonly reportedEntries = signal<ReadonlySet<string>>(new Set());

  protected readonly entryReported = computed(() => {
    const extension = this.extension();
    return extension !== null && this.reportedEntries().has(entryKey(extension.kind, extension.id));
  });

  private isInstallable(entry: StoreVersionHistoryBody): boolean {
    const extension = this.extension();
    if (!extension || extension.installState === 'Unsupported') {
      return false;
    }
    // An older host sends neither flag and can only install its latest release.
    return entry.installable ?? sameVersion(entry.version, extension.latestVersion);
  }

  private unavailableReason(entry: StoreVersionHistoryBody): string {
    const extension = this.extension();
    return entry.unavailableReason === 'UnsupportedPlatform' || extension?.installState === 'Unsupported'
      ? this.localization.translateKey(AppStrings.Store.NotSupportedOnPlatform)
      : this.localization.translateKey(AppStrings.Store.Page.VersionUnavailableReason);
  }

  protected readonly platforms = computed(() => {
    const supported = this.extension()?.supportedOperatingSystems ?? [];
    return PLATFORMS.map(platform => {
      const available = supported.length === 0 || supported.includes(platform.name);
      return {
        ...platform,
        available,
        label: this.localization.translateKey(
          available ? AppStrings.Store.Page.PlatformSupported : AppStrings.Store.Page.PlatformNotSupported,
          { platform: platform.name }),
      };
    });
  });

  constructor() {
    effect(() => {
      const id = this.extensionId();
      this.ratings.installs();
      untracked(() => void this.ratings.ensure([id]));
    });

    void this.categories.ensure();

    // Mirrors the store page's subscription: an uninstall creates no operation, so nothing else
    // here would notice the extension is gone and stop showing a live Uninstall button for it.
    this.api.onNotification('StoreCatalogChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        void this.loadExtension();
        void this.loadSimilar();
      });
  }

  protected openHistoryModal(event: MouseEvent): void {
    // `shared-button` renders its own native <button> as a child, so the focusable element to
    // return to on close lives one level below the host the (click) listener is bound to.
    const host = event.currentTarget as HTMLElement;
    this.historyTriggerElement = host.querySelector('button') ?? host;
    this.historyModalOpen.set(true);
  }

  protected onHistoryModalClosed(): void {
    this.historyModalOpen.set(false);
    this.historyTriggerElement?.focus();
    this.historyTriggerElement = null;
  }

  protected openLanguagesModal(event: MouseEvent): void {
    const host = event.currentTarget as HTMLElement;
    this.languagesTriggerElement = host.querySelector('button') ?? host;
    this.languagesModalOpen.set(true);
  }

  protected onLanguagesModalClosed(): void {
    this.languagesModalOpen.set(false);
    this.languagesTriggerElement?.focus();
    this.languagesTriggerElement = null;
  }

  async ngOnInit(): Promise<void> {
    const kindParam = this.route.snapshot.paramMap.get('kind') as StoreExtensionKind | null;
    const extensionId = this.route.snapshot.paramMap.get('extensionId') ?? '';

    if (!kindParam || !KNOWN_KINDS.includes(kindParam) || !extensionId) {
      void this.router.navigate(['/store']);
      return;
    }

    const navigation = this.router.currentNavigation() ?? this.router.lastSuccessfulNavigation();
    const previousUrl = navigation?.previousNavigation?.finalUrl?.toString();
    this.openedInStore = isStoreListUrl(previousUrl) || isStoreDetailUrl(previousUrl);
    this.kind.set(kindParam);
    this.extensionId.set(extensionId);
    if (kindParam === 'Plugin' && this.integrations.integrations().length === 0) {
      void this.integrations.loadIntegrations();
    }
    if (kindParam === 'IconPack' && this.iconPacks.packs().length === 0) {
      void this.iconPacks.loadPacks();
    }
    await this.loadExtension();
    await this.loadSimilar();
  }

  private async loadSimilar(): Promise<void> {
    const kind = this.kind();
    const id = this.extensionId();
    if (!kind || !id) {
      return;
    }
    const generation = ++this.similarGeneration;
    try {
      const response = await this.api.getStoreSimilar(kind, id);
      if (generation === this.similarGeneration) {
        this.similar.set(response.error ? [] : response.items ?? []);
      }
    } catch {
      if (generation === this.similarGeneration) {
        this.similar.set([]);
      }
    }
  }

  protected openTag(tag: string): void {
    void this.router.navigate(['/store'], { queryParams: { tag } });
  }

  protected async onSimilarInstall(item: StoreCatalogItemBody): Promise<void> {
    await this.operations.install(item.kind, item.id);
  }

  protected async onSimilarInstallUnsigned(request: StoreUnsignedInstallRequest): Promise<void> {
    await this.operations.install(request.item.kind, request.item.id, request.version, true);
  }

  protected async onSimilarRetry(item: StoreCatalogItemBody): Promise<void> {
    const operation = this.operations.operationFor(item.kind, item.id)();
    if (operation) {
      await this.operations.retry(operation.id);
    }
  }

  private async loadExtension(): Promise<void> {
    const kind = this.kind();
    const id = this.extensionId();
    if (!kind || !id) {
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const response = await this.api.getStoreExtension(kind, id);
      if (!response.extension) {
        this.loadError.set(
          response.error?.message ?? this.localization.translateKey(AppStrings.Store.Page.ExtensionNotFound));
        return;
      }
      const previous = this.selectedVersion();
      this.extension.set(response.extension);
      if (previous && !response.extension.history.some(entry => sameVersion(entry.version, previous))) {
        this.selectedVersion.set(null);
      }
    } catch (error) {
      console.error('Failed to load store extension:', error);
      this.loadError.set(
        error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Store.Page.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected goBack(): void {
    if (this.openedInStore) {
      this.location.back();
      return;
    }
    void this.router.navigate(['/store']);
  }

  protected onVersionChange(version: string): void {
    this.selectedVersion.set(version);
  }

  protected async onInstall(): Promise<void> {
    const extension = this.extension();
    if (!extension) {
      return;
    }
    const installed = extension.installedVersion;
    if (installed && (compareVersions(this.effectiveVersion(), installed) ?? 0) < 0) {
      this.downgradeConfirmOpen.set(true);
      return;
    }
    await this.installSelected();
  }

  protected async confirmDowngrade(): Promise<void> {
    this.downgradeConfirmOpen.set(false);
    await this.installSelected();
  }

  protected cancelDowngrade(): void {
    this.downgradeConfirmOpen.set(false);
  }

  private async installSelected(): Promise<void> {
    const kind = this.kind();
    const extension = this.extension();
    if (!kind || !extension) {
      return;
    }
    // The latest is requested without a version so the host treats it as an ordinary, unpinned install.
    await this.operations.install(kind, extension.id, this.selectedIsLatest() ? undefined : this.effectiveVersion() ?? undefined);
  }

  protected async onInstallUnsigned(version: string | undefined): Promise<void> {
    const kind = this.kind();
    const extension = this.extension();
    if (!kind || !extension) {
      return;
    }
    await this.operations.install(kind, extension.id, version, true);
  }

  protected onManage(action: StoreManageAction): void {
    if (action === 'settings') {
      void this.router.navigate(['/integrations', this.extensionId()]);
      return;
    }
    const packId = this.installedIconPackId();
    if (packId) {
      void this.router.navigate(['/library/icon-packs'], { queryParams: { pack: packId } });
    }
  }

  protected onCheckForUpdates(): void {
    void this.appUpdates.check();
    this.updateModal.open();
  }

  protected openLink(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.externalLinks.open(url);
  }

  protected scrollToReviews(): void {
    this.element.nativeElement.querySelector<HTMLElement>('app-store-reviews-section')
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected onHistoryVersionSelected(version: string): void {
    this.selectedVersion.set(version);
    this.onHistoryModalClosed();
  }

  protected async onRetry(): Promise<void> {
    const operation = this.operation();
    if (operation) {
      await this.operations.retry(operation.id);
    }
  }

  protected requestEntryReport(): void {
    const extension = this.extension();
    if (!extension) {
      return;
    }
    const status = this.account.session()?.status;
    if (status === 'signedOut' || status === 'reauthenticationRequired') {
      this.settingsModal.open('account');
      return;
    }
    this.reportTarget.set({ type: 'entry', name: extension.name });
  }

  protected closeEntryReport(): void {
    this.reportTarget.set(null);
  }

  protected onEntryReported(): void {
    const extension = this.extension();
    if (extension) {
      this.reportedEntries.update(keys => new Set([...keys, entryKey(extension.kind, extension.id)]));
      afterNextRender(() => this.element.nativeElement.querySelector<HTMLElement>('.detail-reported')?.focus(),
        { injector: this.injector });
    }
    this.reportTarget.set(null);
  }

  protected onUninstallRequested(): void {
    this.uninstallConfirmOpen.set(true);
  }

  protected cancelUninstall(): void {
    this.uninstallConfirmOpen.set(false);
  }

  protected async confirmUninstall(): Promise<void> {
    this.uninstallConfirmOpen.set(false);
    const kind = this.kind();
    const extension = this.extension();
    if (!kind || !extension) {
      return;
    }
    const response = await this.operations.uninstall(kind, extension.id);
    if (!response.success) {
      const detailKey = storeUninstallErrorKey(response.error?.code);
      this.toasts.show(this.localization.translateKey(AppStrings.Store.UninstallFailed), {
        detail: detailKey ? this.localization.translateKey(detailKey) : undefined,
        variant: 'error',
      });
    }
  }
}

function isHttps(url: string): boolean {
  try {
    return new URL(url).protocol === 'https:';
  } catch {
    return false;
  }
}

function entryKey(kind: StoreExtensionKind, id: string): string {
  return `${kind}:${id}`;
}

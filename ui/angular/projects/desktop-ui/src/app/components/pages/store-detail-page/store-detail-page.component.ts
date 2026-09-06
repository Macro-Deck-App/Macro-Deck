import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { AppStrings, StoreExtensionDetailBody, StoreExtensionKind, StoreVersionHistoryBody } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { DetailPageComponent } from '../../detail-page/detail-page.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { cultureDisplayName, sortCulturesForReader } from '../../../localization/culture-display.util';
import { StoreOperationService } from '../../../services/store-operation.service';
import { formatBytes } from '../../../util/format-bytes';
import { storeUninstallErrorKey, storeUninstallMessageKey } from '../../../util/store-operation-display';
import { StoreDetailHeaderComponent } from './store-detail-header.component';
import { StoreLanguagesModalComponent } from './store-languages-modal.component';
import { StoreScreenshotStripComponent } from './store-screenshot-strip.component';
import { StoreVersionHistoryModalComponent } from './store-version-history-modal.component';

const KNOWN_KINDS: readonly StoreExtensionKind[] = ['Plugin', 'IconPack', 'ProfileTemplate'];

const SIDEBAR_LANGUAGE_LIMIT = 4;

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
    StoreLanguagesModalComponent,
    StoreScreenshotStripComponent,
    StoreMarkdownComponent,
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
  protected readonly operations = inject(StoreOperationService);

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

  protected readonly hasVersionHistory = computed(() => (this.extension()?.history.length ?? 0) > 1);

  protected readonly historyModalOpen = signal(false);
  private historyTriggerElement: HTMLElement | null = null;

  protected readonly languages = computed(() =>
    sortCulturesForReader(this.extension()?.languages ?? [], this.localization.culture()));

  protected readonly visibleLanguages = computed(() => this.languages().slice(0, SIDEBAR_LANGUAGE_LIMIT));

  protected readonly hasMoreLanguages = computed(() => this.languages().length > SIDEBAR_LANGUAGE_LIMIT);

  protected readonly languagesModalOpen = signal(false);
  private languagesTriggerElement: HTMLElement | null = null;

  protected readonly cultureDisplayName = cultureDisplayName;

  protected compatibilityLabel(operatingSystems: readonly string[]): string {
    return operatingSystems.length > 0
      ? operatingSystems.join(', ')
      : this.localization.translateKey(AppStrings.Store.Page.AllPlatforms);
  }

  constructor() {
    // Mirrors the store page's subscription: an uninstall creates no operation, so nothing else
    // here would notice the extension is gone and stop showing a live Uninstall button for it.
    this.api.onNotification('StoreCatalogChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        void this.loadExtension();
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

    this.kind.set(kindParam);
    this.extensionId.set(extensionId);
    await this.loadExtension();
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
      this.extension.set(response.extension);
    } catch (error) {
      console.error('Failed to load store extension:', error);
      this.loadError.set(
        error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Store.Page.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected goBack(): void {
    void this.router.navigate(['/store']);
  }

  protected async onInstall(): Promise<void> {
    const kind = this.kind();
    const extension = this.extension();
    if (!kind || !extension) {
      return;
    }
    await this.operations.install(kind, extension.id);
  }

  protected async onInstallUnsigned(): Promise<void> {
    const kind = this.kind();
    const extension = this.extension();
    if (!kind || !extension) {
      return;
    }
    await this.operations.install(kind, extension.id, undefined, true);
  }

  protected async onRetry(): Promise<void> {
    const operation = this.operation();
    if (operation) {
      await this.operations.retry(operation.id);
    }
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

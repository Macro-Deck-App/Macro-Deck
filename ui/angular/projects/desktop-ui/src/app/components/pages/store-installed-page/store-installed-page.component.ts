import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppStrings, StoreCatalogItemBody, StoreExtensionKind } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { StoreSectionComponent } from '../../store/store-section.component';
import { StoreViewSwitcherComponent } from '../../store/store-view-switcher.component';
import { StoreAccessService } from '../../../services/store-access.service';
import { StoreOperationService } from '../../../services/store-operation.service';
import { StoreUpdatesService } from '../../../services/store-updates.service';
import { storeUninstallErrorKey, storeUninstallMessageKey } from '../../../util/store-operation-display';

const INSTALLED_KINDS: StoreExtensionKind[] = ['Plugin', 'IconPack'];

const PAGE_SIZE = 100;

@Component({
  selector: 'app-store-installed-page',
  standalone: true,
  imports: [
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    StoreSectionComponent,
    StoreViewSwitcherComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-installed-page.component.html',
  styleUrls: ['../store-page/store-page.component.scss'],
})
export class StoreInstalledPageComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);
  protected readonly operations = inject(StoreOperationService);
  protected readonly updates = inject(StoreUpdatesService);
  protected readonly storeUnlocked = inject(StoreAccessService).unlocked;

  protected readonly items = signal<StoreCatalogItemBody[]>([]);
  protected readonly total = signal(0);
  protected readonly loaded = signal(false);
  protected readonly loadingMore = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly pendingUninstall = signal<StoreCatalogItemBody | null>(null);

  protected readonly hasMore = computed(() => this.items().length < this.total());

  protected readonly updatesSummary = computed(() => this.updates.count() > 0
    ? this.localization.translateKey(AppStrings.Store.Page.InstalledUpdatesAvailable, { count: this.updates.count() })
    : this.localization.translateKey(AppStrings.Store.Page.InstalledUpToDate));

  protected readonly updateAllBusy = computed(() =>
    this.updates.installing() || this.updates.updates().every(update => {
      const operation = this.operations.operationFor(update.kind, update.packageId)();
      return operation !== null && !['Completed', 'Failed', 'Cancelled'].includes(operation.state);
    }));

  protected readonly uninstallMessage = computed(() => {
    const item = this.pendingUninstall();
    return item ? this.localization.translateKey(storeUninstallMessageKey(item.kind), { name: item.name }) : '';
  });

  private generation = 0;

  constructor() {
    this.api.onNotification('StoreCatalogChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async onUpdateAll(): Promise<void> {
    await this.updates.installAll();
  }

  protected async onInstall(item: StoreCatalogItemBody): Promise<void> {
    await this.operations.install(item.kind, item.id);
  }

  protected async onInstallUnsigned(item: StoreCatalogItemBody): Promise<void> {
    await this.operations.install(item.kind, item.id, undefined, true);
  }

  protected async onRetry(item: StoreCatalogItemBody): Promise<void> {
    const operation = this.operations.operationFor(item.kind, item.id)();
    if (operation) {
      await this.operations.retry(operation.id);
    }
  }

  protected onUninstallRequested(item: StoreCatalogItemBody): void {
    this.pendingUninstall.set(item);
  }

  protected cancelUninstall(): void {
    this.pendingUninstall.set(null);
  }

  protected async confirmUninstall(): Promise<void> {
    const item = this.pendingUninstall();
    this.pendingUninstall.set(null);
    if (!item) {
      return;
    }

    const response = await this.operations.uninstall(item.kind, item.id);
    if (!response.success) {
      const detailKey = storeUninstallErrorKey(response.error?.code);
      this.toasts.show(this.localization.translateKey(AppStrings.Store.UninstallFailed), {
        detail: detailKey ? this.localization.translateKey(detailKey) : undefined,
        variant: 'error',
      });
    }
  }

  protected async onLoadMore(): Promise<void> {
    if (this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);
    const generation = this.generation;
    try {
      const response = await this.api.getStoreCatalog({
        kinds: INSTALLED_KINDS,
        section: 'name',
        installed: true,
        skip: this.items().length,
        take: PAGE_SIZE,
      });
      if (generation === this.generation) {
        this.items.update(items => [...items, ...(response.items ?? [])]);
        this.total.set(response.total ?? 0);
      }
    } catch (error) {
      this.loadError.set(error instanceof Error ? error.message : String(error));
    } finally {
      this.loadingMore.set(false);
    }
  }

  private async load(): Promise<void> {
    const generation = ++this.generation;
    this.loadError.set(null);
    try {
      const response = await this.api.getStoreCatalog({
        kinds: INSTALLED_KINDS,
        section: 'name',
        installed: true,
        take: PAGE_SIZE,
      });
      if (generation !== this.generation) {
        return;
      }
      this.items.set(response.items ?? []);
      this.total.set(response.total ?? 0);
    } catch (error) {
      if (generation === this.generation) {
        this.loadError.set(error instanceof Error ? error.message : String(error));
      }
    } finally {
      if (generation === this.generation) {
        this.loaded.set(true);
      }
    }
  }
}

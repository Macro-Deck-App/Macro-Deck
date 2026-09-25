import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AppStrings, StoreCatalogItemBody, StoreOperationBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe } from '@shared';
import { PluginRuntimeService } from '../../services/plugin-runtime.service';
import { StoreRatingsService } from '../../services/store-ratings.service';
import { storeFreshness } from '../../util/store-badges';
import { formatStoreCount, formatStoreInstallCount, formatStoreRating } from '../../util/store-rating-format';
import { storeKindIcon, storeKindLabelKey, storeTrustLabelKey } from '../../util/store-operation-display';
import { StoreInstallButtonComponent } from './store-install-button.component';
import { StoreRatingStarsComponent } from './store-rating-stars.component';

@Component({
  selector: 'shared-store-extension-card',
  standalone: true,
  imports: [NgTemplateOutlet, RouterLink, StoreInstallButtonComponent, StoreRatingStarsComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-extension-card.component.html',
  styleUrls: ['./store-extension-card.component.scss'],
})
export class StoreExtensionCardComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly runtime = inject(PluginRuntimeService);
  private readonly ratings = inject(StoreRatingsService);
  private readonly router = inject(Router);

  readonly item = input.required<StoreCatalogItemBody>();
  readonly variant = input<'compact' | 'tile'>('compact');
  readonly featured = input(false);
  readonly updatesAvailable = input(false);

  protected readonly takenOver = computed(() => {
    const item = this.item();
    return item.kind === 'Plugin' &&
      this.runtime.plugins().some(plugin => plugin.pluginId === item.id && plugin.takenOverByDevelopmentBuild === true);
  });

  protected readonly takenOverLabel = computed(() =>
    this.localization.translateKey(AppStrings.Developer.ManagedPlugins.TakenOverBadge));

  protected readonly withdrawalChip = computed(() => {
    const item = this.item();
    const withdrawal = item.installedVersionWithdrawal ?? (item.installedVersion ? item.withdrawal : null);
    if (!withdrawal) {
      return null;
    }
    const label = item.installedVersionWithdrawal
      ? AppStrings.Store.Withdrawal.VersionChip
      : AppStrings.Store.Withdrawal.PackageChip;
    return {
      label: this.localization.translateKey(label),
      reason: withdrawal.reason
        ? this.localization.translateKey(AppStrings.Store.Withdrawal.Reason, { reason: withdrawal.reason })
        : null,
    };
  });
  readonly operation = input<StoreOperationBody | null>(null);

  readonly install = output<void>();
  readonly installUnsigned = output<string | undefined>();
  readonly retry = output<void>();
  readonly uninstall = output<void>();
  readonly checkForUpdates = output<void>();

  protected readonly iconUrl = computed(() => {
    const item = this.item();
    return item.hasIcon ? this.api.getStoreExtensionIconUrl(item.kind, item.id, item.iconSha256) : null;
  });

  protected readonly artworkUrl = computed(() => {
    const item = this.item();
    return item.previewScreenshotSha256
      ? this.api.getStoreScreenshotUrl(item.kind, item.id, 0, item.previewScreenshotSha256)
      : null;
  });

  protected readonly artworkFailed = signal(false);

  protected readonly freshness = computed(() => storeFreshness(this.item()));

  protected readonly detailLink = computed(() => ['/store', this.item().kind, this.item().id]);

  // A missing icon 404s legitimately (the extension never published one) - the flag just tracks
  // that so a load failure falls back to the kind glyph instead of a broken image.
  protected readonly iconFailed = signal(false);

  protected readonly kindIcon = computed(() => storeKindIcon(this.item().kind));

  protected readonly publisherText = computed(() => {
    const publisher = this.item().publisher;
    return publisher
      ? this.localization.translateKey(AppStrings.Store.Page.ByPublisher, { publisher })
      : null;
  });

  protected readonly kindLabel = computed(() => {
    const key = storeKindLabelKey(this.item().kind);
    return key ? this.localization.translateKey(key) : '';
  });

  protected readonly revokedLabel = computed(() => this.localization.translateKey(AppStrings.Store.SigningRevoked));

  protected readonly revokedDescription = computed(() =>
    this.localization.translateKey(AppStrings.Plugins.TrustRefusal.Revoked));

  protected readonly trustLabel = computed(() => {
    const key = storeTrustLabelKey(this.item().kind, this.item().trust);
    return key ? this.localization.translateKey(key) : null;
  });

  protected readonly rating = computed(() => {
    const summary = this.ratings.ratings().get(this.item().id);
    if (!summary || summary.ratingCount <= 0 || summary.rating === null || summary.rating === undefined) {
      return null;
    }
    return {
      value: summary.rating,
      text: formatStoreRating(summary.rating, this.localization.culture()),
      count: this.localization.translateKey(AppStrings.Store.Reviews.RatingCount, { count: summary.ratingCount }),
    };
  });

  protected readonly installs = computed(() => {
    const count = this.ratings.installs().get(this.item().id);
    if (!count || count <= 0) {
      return null;
    }
    const culture = this.localization.culture();
    return {
      text: formatStoreInstallCount(count, culture),
      label: this.localization.translateKey(AppStrings.Store.Page.InstallCount, {
        count,
        installs: formatStoreCount(count, culture),
      }),
    };
  });

  constructor() {
    effect(() => {
      this.iconUrl();
      this.iconFailed.set(false);
    });
    effect(() => {
      this.artworkUrl();
      this.artworkFailed.set(false);
    });
  }

  protected onIconError(): void {
    this.iconFailed.set(true);
  }

  protected onArtworkError(): void {
    this.artworkFailed.set(true);
  }

  protected openDetails(): void {
    void this.router.navigate(this.detailLink());
  }
}

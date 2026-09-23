import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppStrings, StoreExtensionDetailBody, StoreOperationBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe } from '@shared';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { StoreInstallButtonComponent, StoreManageAction } from '../../store/store-install-button.component';
import { StoreRatingStarsComponent } from '../../store/store-rating-stars.component';
import { StoreRatingsService } from '../../../services/store-ratings.service';
import { storeFreshness } from '../../../util/store-badges';
import { formatStoreCount, formatStoreRating } from '../../../util/store-rating-format';
import { storeKindIcon, storeKindLabelKey } from '../../../util/store-operation-display';
import { StoreTrustBadgeComponent } from './store-trust-badge.component';

@Component({
  selector: 'app-store-detail-header',
  standalone: true,
  imports: [FormsModule, RouterLink, SelectComponent, StoreInstallButtonComponent, StoreRatingStarsComponent, StoreTrustBadgeComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-detail-header.component.html',
  styleUrls: ['./store-detail-header.component.scss'],
})
export class StoreDetailHeaderComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly ratings = inject(StoreRatingsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly extension = input.required<StoreExtensionDetailBody>();
  readonly operation = input<StoreOperationBody | null>(null);
  readonly versionOptions = input<SelectOption[]>([]);
  readonly selectedVersion = input<string | null>(null);
  readonly targetInstallable = input(true);
  readonly targetUnavailableReason = input<string | null>(null);
  readonly otherVersionsAvailable = input(false);
  readonly updatesAvailable = input(false);
  readonly versionHints = input<string[]>([]);
  readonly manageAction = input<StoreManageAction | null>(null);

  readonly install = output<void>();
  readonly installUnsigned = output<string | undefined>();
  readonly retry = output<void>();
  readonly uninstall = output<void>();
  readonly checkForUpdates = output<void>();
  readonly versionChange = output<string>();
  readonly showReviews = output<void>();
  readonly manage = output<StoreManageAction>();

  private readonly versionSelect = viewChild(SelectComponent, { read: ElementRef });

  protected readonly iconUrl = computed(() => {
    const extension = this.extension();
    return extension.hasIcon ? this.api.getStoreExtensionIconUrl(extension.kind, extension.id, extension.iconSha256) : null;
  });

  // A missing icon 404s legitimately (the extension never published one) - the flag just tracks
  // that so a load failure falls back to the kind glyph instead of a broken image.
  protected readonly iconFailed = signal(false);

  protected readonly kindLabel = computed(() => {
    const key = storeKindLabelKey(this.extension().kind);
    return key ? this.localization.translateKey(key) : '';
  });
  protected readonly kindIcon = computed(() => storeKindIcon(this.extension().kind));

  protected readonly freshness = computed(() => storeFreshness(this.extension()));

  protected readonly aiBadge = computed<'uses' | 'made' | null>(() => {
    const ai = this.extension().ai;
    if (!ai) {
      return null;
    }
    if (ai.interaction || ai.generatedContent || (ai.services ?? []).length > 0) {
      return 'uses';
    }
    return ai.generatedAssets ? 'made' : null;
  });

  protected readonly rating = computed(() => {
    const summary = this.ratings.ratings().get(this.extension().id);
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
    const count = this.ratings.installs().get(this.extension().id);
    return count && count > 0
      ? this.localization.translateKey(AppStrings.Store.Page.InstallCount, {
        count,
        installs: formatStoreCount(count, this.localization.culture()),
      })
      : null;
  });

  protected readonly showVersionSelect = computed(() => this.versionOptions().length > 1);

  constructor() {
    effect(() => {
      this.iconUrl();
      this.iconFailed.set(false);
    });
  }

  protected onIconError(): void {
    this.iconFailed.set(true);
  }

  protected focusVersionSelect(): void {
    const select: HTMLElement = this.versionSelect()?.nativeElement ?? this.host.nativeElement;
    select.querySelector<HTMLElement>('button, [tabindex]')?.focus();
  }
}

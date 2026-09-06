import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppStrings, StoreCatalogItemBody, StoreOperationBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { storeKindIcon, storeKindLabelKey, storeTrustLabelKey } from '../../util/store-operation-display';
import { StoreStateBadgeComponent } from './store-state-badge.component';
import { StoreInstallButtonComponent } from './store-install-button.component';

@Component({
  selector: 'shared-store-extension-card',
  standalone: true,
  imports: [RouterLink, StoreStateBadgeComponent, StoreInstallButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-extension-card.component.html',
  styleUrls: ['./store-extension-card.component.scss'],
})
export class StoreExtensionCardComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly item = input.required<StoreCatalogItemBody>();
  readonly operation = input<StoreOperationBody | null>(null);

  readonly install = output<void>();
  readonly installUnsigned = output<void>();
  readonly retry = output<void>();
  readonly uninstall = output<void>();

  protected readonly iconUrl = computed(() => {
    const item = this.item();
    return item.hasIcon ? this.api.getStoreExtensionIconUrl(item.kind, item.id) : null;
  });

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

  protected readonly trustLabel = computed(() => {
    const key = storeTrustLabelKey(this.item().kind, this.item().trust);
    return key ? this.localization.translateKey(key) : null;
  });

  constructor() {
    effect(() => {
      this.iconUrl();
      this.iconFailed.set(false);
    });
  }

  protected onIconError(): void {
    this.iconFailed.set(true);
  }
}

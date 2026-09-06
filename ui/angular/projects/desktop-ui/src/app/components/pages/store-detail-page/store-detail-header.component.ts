import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { StoreExtensionDetailBody, StoreOperationBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe } from '@shared';
import { StoreInstallButtonComponent } from '../../store/store-install-button.component';
import { StoreStateBadgeComponent } from '../../store/store-state-badge.component';
import { storeKindIcon, storeKindLabelKey } from '../../../util/store-operation-display';
import { StoreTrustBadgeComponent } from './store-trust-badge.component';

@Component({
  selector: 'app-store-detail-header',
  standalone: true,
  imports: [StoreStateBadgeComponent, StoreTrustBadgeComponent, StoreInstallButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-detail-header.component.html',
  styleUrls: ['./store-detail-header.component.scss'],
})
export class StoreDetailHeaderComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly extension = input.required<StoreExtensionDetailBody>();
  readonly operation = input<StoreOperationBody | null>(null);

  readonly install = output<void>();
  readonly installUnsigned = output<void>();
  readonly retry = output<void>();
  readonly uninstall = output<void>();

  protected readonly iconUrl = computed(() => {
    const extension = this.extension();
    return extension.hasIcon ? this.api.getStoreExtensionIconUrl(extension.kind, extension.id) : null;
  });

  // A missing icon 404s legitimately (the extension never published one) - the flag just tracks
  // that so a load failure falls back to the kind glyph instead of a broken image.
  protected readonly iconFailed = signal(false);

  protected readonly kindLabel = computed(() => {
    const key = storeKindLabelKey(this.extension().kind);
    return key ? this.localization.translateKey(key) : '';
  });
  protected readonly kindIcon = computed(() => storeKindIcon(this.extension().kind));

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

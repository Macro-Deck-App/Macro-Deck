import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { StoreExtensionKind, StoreExtensionTrust } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { storeTrustLabelKey } from '../../../util/store-operation-display';

@Component({
  selector: 'app-store-trust-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (label(); as text) {
      <span class="trust-badge">
        <span class="icon icon-check icon-xs" aria-hidden="true"></span>
        {{ text }}
      </span>
    }
  `,
  styleUrls: ['./store-trust-badge.component.scss'],
})
export class StoreTrustBadgeComponent {
  readonly kind = input.required<StoreExtensionKind>();
  readonly trust = input.required<StoreExtensionTrust | string>();

  private readonly localization = inject(LocalizationService);

  protected readonly label = computed(() => {
    const key = storeTrustLabelKey(this.kind(), this.trust());
    return key ? this.localization.translateKey(key) : null;
  });
}

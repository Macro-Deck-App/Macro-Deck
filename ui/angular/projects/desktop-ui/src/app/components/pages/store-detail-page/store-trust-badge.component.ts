import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AppStrings, StoreExtensionKind, StoreExtensionTrust } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { PluginRuntimeService } from '../../../services/plugin-runtime.service';
import { storeTrustLabelKey } from '../../../util/store-operation-display';

@Component({
  selector: 'app-store-trust-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (signingRevoked()) {
      <span class="revoked-badge" [attr.title]="revokedDescription()">
        <span class="icon icon-alert-triangle icon-xs" aria-hidden="true"></span>
        {{ revokedLabel() }}
      </span>
    } @else if (takenOver()) {
      <span class="takeover-badge">
        <span class="icon icon-alert-triangle icon-xs" aria-hidden="true"></span>
        {{ takenOverLabel() }}
      </span>
    } @else if (label(); as text) {
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
  readonly pluginId = input<string | null>(null);
  readonly signingRevoked = input(false);

  private readonly localization = inject(LocalizationService);
  private readonly runtime = inject(PluginRuntimeService);

  protected readonly takenOver = computed(() => {
    const pluginId = this.pluginId();
    return this.kind() === 'Plugin' && pluginId !== null &&
      this.runtime.plugins().some(plugin => plugin.pluginId === pluginId && plugin.takenOverByDevelopmentBuild === true);
  });

  protected readonly takenOverLabel = computed(() =>
    this.localization.translateKey(AppStrings.Developer.ManagedPlugins.TakenOverBadge));

  protected readonly revokedLabel = computed(() => this.localization.translateKey(AppStrings.Store.SigningRevoked));

  protected readonly revokedDescription = computed(() =>
    this.localization.translateKey(AppStrings.Plugins.TrustRefusal.Revoked));

  protected readonly label = computed(() => {
    const key = storeTrustLabelKey(this.kind(), this.trust());
    return key ? this.localization.translateKey(key) : null;
  });
}

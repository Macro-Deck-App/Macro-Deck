import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AppStrings, StoreInstallState } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

const STATE_LABEL_KEYS: Record<StoreInstallState, string> = {
  NotInstalled: AppStrings.Store.State.NotInstalled,
  Installed: AppStrings.Store.State.Installed,
  UpdateAvailable: AppStrings.Store.State.UpdateAvailable,
  Unsupported: AppStrings.Store.State.Unsupported,
};

const STATE_TIERS: Record<StoreInstallState, 'ok' | 'accent' | 'neutral'> = {
  NotInstalled: 'neutral',
  Installed: 'ok',
  UpdateAvailable: 'accent',
  Unsupported: 'neutral',
};

@Component({
  selector: 'shared-store-state-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="badge" [class]="'badge-' + tier()" [class.badge-quiet]="tone() === 'quiet'" [title]="title()">
      {{ label() }}
    </span>
  `,
  styleUrls: ['./store-state-badge.component.scss'],
})
export class StoreStateBadgeComponent {
  readonly state = input.required<StoreInstallState>();

  readonly tone = input<'default' | 'quiet'>('default');

  private readonly localization = inject(LocalizationService);

  protected readonly label = computed(() => {
    const key = STATE_LABEL_KEYS[this.state()];
    return key ? this.localization.translateKey(key) : this.state();
  });
  protected readonly tier = computed(() => STATE_TIERS[this.state()] ?? 'neutral');

  // The host's own unsupported reason names the runtime identifier it checked and is English; the
  // badge says the same thing in the reader's language instead.
  protected readonly title = computed(() => this.state() === 'Unsupported'
    ? this.localization.translateKey(AppStrings.Store.NotSupportedOnPlatform)
    : null);
}

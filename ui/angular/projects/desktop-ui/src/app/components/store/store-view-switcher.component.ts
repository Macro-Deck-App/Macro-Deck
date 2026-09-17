import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, SegmentedControlComponent, SegmentedOption } from '@shared';
import { ConnectAccountService } from '../../services/connect-account.service';

export type StoreView = 'discover' | 'installed' | 'tests';

const STORE_VIEW_ROUTES: Record<StoreView, string> = {
  discover: '/store',
  installed: '/store/installed',
  tests: '/store/tests',
};

@Component({
  selector: 'app-store-view-switcher',
  standalone: true,
  imports: [SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-segmented-control
      [ariaLabel]="ariaLabel()"
      [options]="options()"
      [value]="active()"
      (valueChange)="open($event)" />
  `,
})
export class StoreViewSwitcherComponent {
  readonly active = input.required<StoreView>();

  private readonly router = inject(Router);
  private readonly localization = inject(LocalizationService);
  private readonly account = inject(ConnectAccountService);

  protected readonly ariaLabel = computed(() => this.localization.translateKey(AppStrings.Store.Page.TabsAriaLabel));

  protected readonly options = computed<SegmentedOption[]>(() => [
    { value: 'discover', label: this.localization.translateKey(AppStrings.Store.Page.TabDiscover) },
    { value: 'installed', label: this.localization.translateKey(AppStrings.Store.Page.TabInstalled) },
    // Test builds come from the Platform for the signed-in account, so there is nothing to show without one.
    ...(this.account.isSignedIn()
      ? [{ value: 'tests', label: this.localization.translateKey(AppStrings.Store.Tests.Title) }]
      : []),
  ]);

  protected open(value: string): void {
    const route = STORE_VIEW_ROUTES[value as StoreView];
    if (route && value !== this.active()) {
      void this.router.navigateByUrl(route);
    }
  }
}

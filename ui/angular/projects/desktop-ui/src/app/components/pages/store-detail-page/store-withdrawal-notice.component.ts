import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { StoreExtensionDetailBody } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';

@Component({
  selector: 'app-store-withdrawal-notice',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-withdrawal-notice.component.html',
  styleUrls: ['./store-withdrawal-notice.component.scss'],
})
export class StoreWithdrawalNoticeComponent {
  readonly extension = input.required<StoreExtensionDetailBody>();

  protected readonly notice = computed(() => {
    const extension = this.extension();
    if (extension.installedVersionWithdrawal && extension.installedVersion) {
      return {
        installedVersion: extension.installedVersion,
        withdrawal: extension.installedVersionWithdrawal,
        packageWithdrawn: !!extension.withdrawal,
      };
    }
    if (extension.withdrawal && extension.installedVersion) {
      return { installedVersion: null, withdrawal: extension.withdrawal, packageWithdrawn: true };
    }
    return null;
  });
}

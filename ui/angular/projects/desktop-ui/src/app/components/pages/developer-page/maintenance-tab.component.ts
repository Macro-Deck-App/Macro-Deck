import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, TranslatePipe } from '@shared';
import { CopyValueComponent } from '../../copy-value/copy-value.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { DataDirectoryService, RestartNoticeService } from '../../../services';

@Component({
  selector: 'app-maintenance-tab',
  standalone: true,
  imports: [ButtonComponent, ConfirmationModalComponent, CopyValueComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './maintenance-tab.component.html',
  styleUrls: ['./maintenance-tab.component.scss'],
})
export class MaintenanceTabComponent {
  private readonly restartNotice = inject(RestartNoticeService);
  protected readonly localization = inject(LocalizationService);
  protected readonly dataDirectory = inject(DataDirectoryService);

  protected readonly restartPromptOpen = signal(false);

  protected readonly canRestart = this.restartNotice.canRestart;
  protected readonly restarting = this.restartNotice.restarting;
  protected readonly unsupportedReason = this.restartNotice.unsupportedReason;
  protected readonly cannotRestartHereFallback = computed(() =>
    this.localization.translateKey(AppStrings.Shell.RestartNotice.CannotRestartHere));

  constructor() {
    void this.restartNotice.refresh();
    void this.dataDirectory.refresh();
  }

  askToRestart(): void {
    this.restartPromptOpen.set(true);
  }

  cancelRestart(): void {
    this.restartPromptOpen.set(false);
  }

  async restartNow(): Promise<void> {
    this.restartPromptOpen.set(false);
    await this.restartNotice.restartNow('developer');
  }
}

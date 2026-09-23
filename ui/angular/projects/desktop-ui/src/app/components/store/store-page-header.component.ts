import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslatePipe } from '@shared';
import { StoreCatalogService } from '../../services/store-catalog.service';
import { SettingsModalService } from '../../services/settings-modal.service';
import { DropdownMenuComponent } from '../overlay/dropdown-menu/dropdown-menu.component';
import { StoreRegistryRefreshModalComponent } from '../pages/store-page/store-registry-refresh-modal.component';
import { StoreView, StoreViewSwitcherComponent } from './store-view-switcher.component';

@Component({
  selector: 'app-store-page-header',
  standalone: true,
  imports: [DropdownMenuComponent, StoreRegistryRefreshModalComponent, StoreViewSwitcherComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-page-header.component.html',
  styleUrls: ['./store-page-header.component.scss'],
})
export class StorePageHeaderComponent {
  protected readonly catalog = inject(StoreCatalogService);
  private readonly settingsModal = inject(SettingsModalService);

  readonly active = input.required<StoreView>();
  readonly subtitle = input<string | null>(null);

  readonly refreshed = output<void>();

  protected readonly menuOpen = signal(false);
  protected readonly refreshLogOpen = signal(false);

  // The run that was current when this window asked for a new refresh belongs to an earlier refresh,
  // so the log shows nothing from it until the host has reported the new run.
  private readonly supersededRunId = signal<string | null>(null);

  protected readonly refreshLogRun = computed(() => {
    const run = this.catalog.refreshRun();
    return run && run.id === this.supersededRunId() ? null : run;
  });

  async refresh(): Promise<void> {
    this.menuOpen.set(false);
    this.refreshLogOpen.set(true);
    if (this.catalog.refreshing()) {
      return;
    }

    this.supersededRunId.set(this.catalog.refreshRun()?.id ?? null);
    await this.catalog.refreshRegistry();
    this.refreshed.emit();
  }

  protected openSettings(): void {
    this.menuOpen.set(false);
    this.settingsModal.open('extensions');
  }
}

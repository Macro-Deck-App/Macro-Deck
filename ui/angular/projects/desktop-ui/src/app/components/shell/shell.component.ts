import { AfterViewInit, Component, DestroyRef, ElementRef, ViewChild, computed, inject, ChangeDetectionStrategy } from '@angular/core';

import { RouterOutlet } from '@angular/router';
import { SharedStoreProgressComponent } from '../store/shared-store-progress.component';
import {
  NavigationService,
  SIDEBAR_COLLAPSED_WIDTH,
  SettingsModalService,
  UpdateModalService,
  isWorkspaceConstrained,
} from '../../services';
import { StatusbarComponent } from './statusbar/statusbar.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { SettingsModalComponent } from './settings-modal/settings-modal.component';
import { UpdateModalComponent } from './update-modal/update-modal.component';
import { ConnectionPanelComponent } from './connection-panel/connection-panel.component';
import { NotificationPanelComponent } from './notification-panel/notification-panel.component';
import { FooterBarComponent } from './footer-bar/footer-bar.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    StatusbarComponent,
    SidebarComponent,
    SettingsModalComponent,
    UpdateModalComponent,
    ConnectionPanelComponent,
    NotificationPanelComponent,
    FooterBarComponent,
    SharedStoreProgressComponent,
  ],
  templateUrl: './shell.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./shell.component.scss']
})
export class ShellComponent implements AfterViewInit {
  protected readonly navService = inject(NavigationService);
  protected readonly settingsModal = inject(SettingsModalService);
  protected readonly updateModal = inject(UpdateModalService);

  protected readonly collapsedSidebarWidthCss = `${SIDEBAR_COLLAPSED_WIDTH}px`;

  // The full sidebar+main row: its width does not depend on the sidebar's own collapsed state,
  // so measuring it (rather than .shell-main) cannot feed back into itself.
  @ViewChild('shellContent') private shellContent?: ElementRef<HTMLElement>;

  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resizeObserver?.disconnect());
  }

  protected readonly activeNavAction = computed(() => {
    if (this.settingsModal.isOpen()) return 'open-settings';
    if (this.navService.isNotificationPanelOpen()) return 'open-notifications';
    return null;
  });

  onNavAction(action: string): void {
    if (action === 'open-settings') {
      this.settingsModal.open();
    } else if (action === 'open-notifications') {
      this.navService.toggleNotificationPanel();
    }
  }

  ngAfterViewInit(): void {
    const element = this.shellContent?.nativeElement;
    if (!element) return;

    this.resizeObserver = new ResizeObserver(entries => {
      const width = entries[0]?.contentRect.width ?? element.getBoundingClientRect().width;
      const rootFontSizePx = parseFloat(getComputedStyle(document.documentElement).fontSize);
      this.navService.setSpaceConstrained(isWorkspaceConstrained(width, rootFontSizePx));
    });
    this.resizeObserver.observe(element);
  }
}

import { AfterViewInit, Component, DestroyRef, ElementRef, ViewChild, computed, effect, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { ModalComponent } from '@shared';

import { RouterOutlet } from '@angular/router';
import { SharedStoreProgressComponent } from '../store/shared-store-progress.component';
import {
  NavigationService,
  PostUpdateChangelogService,
  SIDEBAR_COLLAPSED_WIDTH,
  SettingsModalService,
  UpdateModalService,
  isWorkspaceConstrained,
} from '../../services';
import { StatusbarComponent } from './statusbar/statusbar.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { SettingsModalComponent } from './settings-modal/settings-modal.component';
import { UpdateModalComponent } from './update-modal/update-modal.component';
import { WhatsNewModalComponent } from './whats-new-modal/whats-new-modal.component';
import { AnnouncementModalComponent } from './announcement-modal/announcement-modal.component';
import { StoreRatingPromptModalComponent } from './store-rating-prompt-modal/store-rating-prompt-modal.component';
import { AnnouncementService } from '../../services/announcement.service';
import { StoreRatingPromptService } from '../../services/store-rating-prompt.service';
import { MigrationOfferService } from '../../services/migration-offer.service';
import { MigrationWizardService } from '../../services/migration-wizard.service';
import { OnboardingService } from '../../services/onboarding.service';
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
    WhatsNewModalComponent,
    AnnouncementModalComponent,
    StoreRatingPromptModalComponent,
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
  protected readonly postUpdateChangelog = inject(PostUpdateChangelogService);
  private readonly announcements = inject(AnnouncementService);
  private readonly ratingPrompt = inject(StoreRatingPromptService);
  private readonly onboarding = inject(OnboardingService);
  private readonly migrationOffer = inject(MigrationOfferService);
  private readonly migrationWizard = inject(MigrationWizardService);

  private readonly announcementShown = signal(false);
  private readonly ratingPromptShown = signal(false);

  private readonly shellIdle = computed(() =>
    this.postUpdateChangelog.settled()
    && !this.postUpdateChangelog.isOpen()
    && !this.updateModal.isOpen()
    && !this.settingsModal.isOpen()
    && this.onboarding.state() === 'done'
    && !this.migrationOffer.pending()
    && !this.migrationWizard.isOpen()
    && ModalComponent.openCount() === 0);

  protected readonly announcementVisible = computed(() =>
    this.announcements.pending() !== null && (this.announcementShown() || this.shellIdle()));

  protected readonly ratingPromptVisible = computed(() =>
    this.ratingPrompt.pending() !== null
    && (this.ratingPromptShown() || (this.announcements.pending() === null && this.shellIdle())));

  protected readonly collapsedSidebarWidthCss = `${SIDEBAR_COLLAPSED_WIDTH}px`;

  // The full sidebar+main row: its width does not depend on the sidebar's own collapsed state,
  // so measuring it (rather than .shell-main) cannot feed back into itself.
  @ViewChild('shellContent') private shellContent?: ElementRef<HTMLElement>;

  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resizeObserver?.disconnect());
    void this.postUpdateChangelog.load();
    // Once on screen the announcement counts among the open modals itself, so it stays until dismissed.
    effect(() => this.announcementShown.set(this.announcementVisible()));
    effect(() => this.ratingPromptShown.set(this.ratingPromptVisible()));
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

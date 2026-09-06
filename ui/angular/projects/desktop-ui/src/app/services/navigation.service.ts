import { computed, inject, Injectable, signal } from '@angular/core';
import { AppStrings, Strings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { NavItem } from '../domain/navigation.interface';
import { NotificationCenterService } from './notification-center.service';

export function formatBuildVersion(version: string, isDevelopmentBuild: boolean, commit: string | null): string {
  return isDevelopmentBuild ? `dev/${commit ?? 'unknown'}` : version;
}

export const SIDEBAR_COLLAPSED_KEY = 'md.sidebar.userCollapsed';

function readPersistedCollapsed(): boolean {
  try {
    return localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

function writePersistedCollapsed(collapsed: boolean): void {
  try {
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, collapsed ? '1' : '0');
  } catch {
  }
}

export const SIDEBAR_EXPANDED_WIDTH = 220;
export const SIDEBAR_COLLAPSED_WIDTH = 64;

export const MIN_CONTENT_WIDTH_REM = 44;

export function isWorkspaceConstrained(availableWidth: number, rootFontSizePx: number): boolean {
  return availableWidth - SIDEBAR_EXPANDED_WIDTH < MIN_CONTENT_WIDTH_REM * rootFontSizePx;
}

@Injectable({
  providedIn: 'root'
})
export class NavigationService {
  private readonly notificationCenter = inject(NotificationCenterService);
  private readonly localization = inject(LocalizationService);

  private readonly userCollapsed = signal(readPersistedCollapsed());
  private readonly spaceConstrained = signal(false);
  private readonly constraintOverride = signal(false);

  readonly isSidebarCollapsed = computed(() => {
    return this.userCollapsed() || (this.spaceConstrained() && !this.constraintOverride());
  });

  readonly appVersion = signal(this.localization.translateKey(Strings.Common.Loading));
  readonly isBeta = signal(false);
  readonly isDevelopmentBuild = signal(false);
  private readonly developmentCommit = signal<string | null>(null);
  readonly versionLabel = computed(() => {
    return formatBuildVersion(this.appVersion(), this.isDevelopmentBuild(), this.developmentCommit());
  });

  readonly mainNavGroups = computed<NavItem[][]>(() => {
    const text = (key: string): string => this.localization.translateKey(key);

    return [
      [
        { id: 'deck', label: text(AppStrings.Nav.Deck), icon: 'grid', route: '/deck' },
      ],
      [
        { id: 'scripts', label: text(AppStrings.Nav.Scripts), icon: 'list-play', route: '/scripts' },
        { id: 'automations', label: text(AppStrings.Nav.Automations), icon: 'zap', route: '/automations' },
      ],
      [
        { id: 'variables', label: text(AppStrings.Nav.Variables), icon: 'braces-x', route: '/variables' },
        { id: 'integrations', label: text(AppStrings.Nav.Integrations), icon: 'puzzle', route: '/integrations' },
        { id: 'library', label: text(AppStrings.Nav.Library), icon: 'layers', route: '/library' },
      ],
      [
        { id: 'store', label: text(AppStrings.Nav.Store), icon: 'store', route: '/store' },
      ],
    ];
  });

  readonly bottomNavItems = computed<NavItem[]>(() => [
    {
      id: 'notifications',
      label: this.localization.translateKey(AppStrings.Nav.Notifications),
      icon: 'bell',
      action: 'open-notifications',
      indicator: this.notificationCenter.hasNotifications() ? this.notificationCenter.badgeText() : null,
      activity: this.notificationCenter.hasActiveProgress(),
    },
    { id: 'developer', label: this.localization.translateKey(AppStrings.Nav.DeveloperTools), icon: 'code', route: '/developer' },
    { id: 'settings', label: this.localization.translateKey(Strings.Settings.Title), icon: 'settings', action: 'open-settings' },
  ]);

  readonly isConnectionPanelOpen = signal(false);

  readonly isNotificationPanelOpen = signal(false);

  toggleNotificationPanel(): void {
    const open = !this.isNotificationPanelOpen();
    this.isNotificationPanelOpen.set(open);
    if (open) {
      this.isConnectionPanelOpen.set(false);
    }
  }

  closeNotificationPanel(): void {
    this.isNotificationPanelOpen.set(false);
  }

  toggleConnectionPanel(): void {
    const open = !this.isConnectionPanelOpen();
    this.isConnectionPanelOpen.set(open);
    if (open) {
      this.isNotificationPanelOpen.set(false);
    }
  }

  closeConnectionPanel(): void {
    this.isConnectionPanelOpen.set(false);
  }

  toggleSidebar(): void {
    if (this.isSidebarCollapsed()) {
      this.userCollapsed.set(false);
      if (this.spaceConstrained()) {
        this.constraintOverride.set(true);
      }
      writePersistedCollapsed(false);
    } else {
      this.userCollapsed.set(true);
      this.constraintOverride.set(false);
      writePersistedCollapsed(true);
    }
  }

  setSpaceConstrained(constrained: boolean): void {
    if (constrained === this.spaceConstrained()) return;
    this.spaceConstrained.set(constrained);
    this.constraintOverride.set(false);
  }

  setAppVersion(version: string, isBeta = false, isDevelopmentBuild = false, commit: string | null = null): void {
    this.appVersion.set(version);
    this.isBeta.set(isBeta);
    this.isDevelopmentBuild.set(isDevelopmentBuild);
    this.developmentCommit.set(isDevelopmentBuild ? commit : null);
  }
}

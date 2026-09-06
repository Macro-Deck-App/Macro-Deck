import { ChangeDetectionStrategy, Component, DestroyRef, ViewChild, computed, inject, signal } from '@angular/core';
import { AppStrings, Strings } from '@macro-deck/runtime';
import { ButtonComponent, ModalComponent, LocalizationService, TranslatePipe, dismissModal } from '@shared';
import { AvatarComponent } from '../../account/avatar.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { TooltipDirective } from '../../overlay/tooltip/tooltip.directive';
import { SettingsNavComponent } from '../../settings/settings-nav/settings-nav.component';
import { NavItem, NavItemGroup } from '../../../domain/navigation.interface';
import { ConnectAccountService } from '../../../services/connect-account.service';
import { RestartNoticeService } from '../../../services/restart-notice.service';
import { SettingsCategory, SettingsModalService } from '../../../services/settings-modal.service';
import { AboutSettingsComponent } from './sections/about-settings.component';
import { AccountSettingsComponent } from './sections/account-settings.component';
import { AdbSettingsComponent } from './sections/adb-settings.component';
import { ClientTargetsSettingsComponent } from './sections/client-targets-settings.component';
import { AppearanceSettingsComponent } from './sections/appearance-settings.component';
import { BackupsSettingsComponent } from './sections/backups-settings.component';
import { DeveloperSettingsComponent } from './sections/developer-settings.component';
import { DevicesSettingsComponent } from './sections/devices-settings.component';
import { LanguageSettingsComponent } from './sections/language-settings.component';
import { LoggingSettingsComponent } from './sections/logging-settings.component';
import { MigrationSettingsComponent } from './sections/migration-settings.component';
import { NetworkSettingsComponent } from './sections/network-settings.component';
import { SecuritySettingsComponent } from './sections/security-settings.component';
import { StartupSettingsComponent } from './sections/startup-settings.component';

interface SettingsCategoryItem extends NavItem {
  id: SettingsCategory;
}

interface SettingsCategoryGroup extends NavItemGroup {
  items: SettingsCategoryItem[];
}

export const SETTINGS_RAIL_COLLAPSED_KEY = 'md.settingsRail.userCollapsed';

const RAIL_CONSTRAINED_QUERY = '(max-width: 768px)';

function readPersistedRailCollapsed(): boolean {
  try {
    return localStorage.getItem(SETTINGS_RAIL_COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

function writePersistedRailCollapsed(collapsed: boolean): void {
  try {
    localStorage.setItem(SETTINGS_RAIL_COLLAPSED_KEY, collapsed ? '1' : '0');
  } catch {
  }
}

const PINNED_CATEGORY_LABEL_KEYS: Partial<Record<SettingsCategory, string>> = {
  account: AppStrings.Settings.Modal.AccountNavLabel,
};

@Component({
  selector: 'app-settings-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, LoadingStateComponent, SettingsNavComponent, AvatarComponent,
    TooltipDirective, TranslatePipe,
    AccountSettingsComponent, AppearanceSettingsComponent,
    StartupSettingsComponent, LanguageSettingsComponent,
    SecuritySettingsComponent, NetworkSettingsComponent, DevicesSettingsComponent, AdbSettingsComponent,
    ClientTargetsSettingsComponent,
    BackupsSettingsComponent, MigrationSettingsComponent,
    LoggingSettingsComponent, DeveloperSettingsComponent, AboutSettingsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings-modal.component.html',
  styleUrls: ['./settings-modal.component.scss'],
})
export class SettingsModalComponent {
  private readonly settingsModal = inject(SettingsModalService);
  protected readonly restartNotice = inject(RestartNoticeService);
  protected readonly connect = inject(ConnectAccountService);

  @ViewChild(ModalComponent) private readonly modal?: ModalComponent;

  readonly activeCategory = this.settingsModal.activeCategory;

  private readonly userCollapsed = signal(readPersistedRailCollapsed());
  private readonly spaceConstrained = signal(false);
  private readonly constraintOverride = signal(false);

  readonly railCollapsed = computed(
    () => this.userCollapsed() || (this.spaceConstrained() && !this.constraintOverride()),
  );

  private readonly localization = inject(LocalizationService);

  readonly categoryGroups = computed<SettingsCategoryGroup[]>(() => {
    const text = (key: string): string => this.localization.translateKey(key);

    return [
      {
        label: text(Strings.Settings.General),
        items: [
          { id: 'appearance', label: text(Strings.Settings.Appearance), icon: 'sun' },
          { id: 'startup', label: text(Strings.Settings.Startup), icon: 'power' },
          { id: 'language', label: text(Strings.Settings.Language), icon: 'globe' },
        ],
      },
      {
        label: text(Strings.Settings.Connectivity),
        items: [
          { id: 'network', label: text(Strings.Settings.Network), icon: 'wifi' },
          { id: 'devices', label: text(Strings.Settings.Devices), icon: 'device-desktop' },
          { id: 'client-targets', label: text(AppStrings.Settings.ClientTargets.Heading), icon: 'device-phone' },
        ],
      },
      {
        label: text(Strings.Settings.PrivacyAndSecurity),
        items: [
          { id: 'security', label: text(Strings.Settings.Security), icon: 'lock' },
        ],
      },
      {
        label: text(Strings.Settings.BackupAndData),
        items: [
          { id: 'backups', label: text(Strings.Settings.Backups), icon: 'layers' },
          { id: 'migration', label: text(AppStrings.Migration.NavLabel), icon: 'download' },
        ],
      },
      {
        label: text(Strings.Settings.Advanced),
        items: [
          { id: 'adb', label: text(Strings.Settings.Adb), icon: 'device-phone' },
          { id: 'logging', label: text(Strings.Settings.Logging), icon: 'file-text' },
          { id: 'developer', label: text(Strings.Settings.Developer), icon: 'code' },
        ],
      },
      {
        items: [{ id: 'about', label: text(Strings.Settings.About), icon: 'info' }],
      },
    ];
  });

  readonly activeLabel = computed(() => {
    const id = this.activeCategory();
    const items = this.categoryGroups().flatMap(group => group.items);
    const pinnedKey = PINNED_CATEGORY_LABEL_KEYS[id];
    if (pinnedKey) {
      return this.localization.translateKey(pinnedKey);
    }
    return items.find(category => category.id === id)?.label ?? '';
  });

  readonly accountName = computed(() =>
    this.connect.session()?.account?.displayName ?? this.localization.translateKey(AppStrings.Settings.Modal.AccountDefaultName));

  constructor() {
    void this.restartNotice.refresh();

    const query = window.matchMedia(RAIL_CONSTRAINED_QUERY);
    const destroyRef = inject(DestroyRef);
    const apply = (matches: boolean): void => {
      if (matches === this.spaceConstrained()) {
        return;
      }
      this.spaceConstrained.set(matches);
      this.constraintOverride.set(false);
    };
    apply(query.matches);
    const listener = (event: MediaQueryListEvent): void => apply(event.matches);
    query.addEventListener('change', listener);
    destroyRef.onDestroy(() => query.removeEventListener('change', listener));
  }

  toggleRail(): void {
    if (this.railCollapsed()) {
      this.userCollapsed.set(false);
      if (this.spaceConstrained()) {
        this.constraintOverride.set(true);
      }
      writePersistedRailCollapsed(false);
      return;
    }
    this.userCollapsed.set(true);
    this.constraintOverride.set(false);
    writePersistedRailCollapsed(true);
  }

  selectCategory(id: string): void {
    this.settingsModal.setCategory(id as SettingsCategory);
  }

  selectAccount(): void {
    this.settingsModal.setCategory('account');
  }

  restartNow(): void {
    void this.restartNotice.restartNow();
  }

  close(): void {
    dismissModal(this.modal, () => this.settingsModal.close());
  }

  onModalClose(): void {
    this.settingsModal.close();
  }
}

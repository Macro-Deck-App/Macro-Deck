import { ChangeDetectionStrategy, Component, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatRelativeTime } from '../relative-time.util';
import { AppStrings, Device, DeviceClientType, DeviceFormFactor } from '@macro-deck/runtime';
import { AuthService, ButtonComponent, ButtonGroupComponent, DeviceIdentityService, ErrorBannerComponent, InputComponent, LocalizationService, LocalizedTextPipe, ModalComponent, ProfileService, ToastService, TranslatePipe, dismissModal, LocalizationKey } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../../../overlay/dropdown-menu/dropdown-menu.component';
import { DeviceService } from '../../../../services/device.service';

const FORM_FACTOR_ICONS: Record<DeviceFormFactor, string> = {
  phone: 'device-phone',
  tablet: 'device-tablet',
  desktop: 'device-desktop',
  unknown: 'device-desktop',
};

const CLIENT_TYPE_LABEL_KEYS: Record<DeviceClientType, LocalizationKey> = {
  'web-client': AppStrings.Settings.Devices.ClientTypeWebClient,
  'admin-ui': AppStrings.Settings.Devices.ClientTypeAdminUi,
  native: AppStrings.Settings.Devices.ClientTypeNative,
  provider: AppStrings.Settings.Devices.ClientTypeProvider,
  unknown: AppStrings.Settings.Devices.ClientTypeUnknown,
};

export type DeviceSessionState = 'online' | 'offline' | 'signedOut';

@Component({
  selector: 'app-devices-settings',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    ButtonGroupComponent,
    ConfirmationModalComponent,
    DropdownMenuComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    InputComponent,
    ModalComponent,
    SelectComponent,
    TranslatePipe,
    LocalizedTextPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './devices-settings.component.html',
  styleUrls: ['./devices-settings.component.scss'],
})
export class DevicesSettingsComponent implements OnInit {
  protected readonly deviceService = inject(DeviceService);
  private readonly deviceIdentity = inject(DeviceIdentityService);
  private readonly authService = inject(AuthService);
  private readonly profileService = inject(ProfileService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly thisDeviceId = computed(() => this.authService.currentDeviceId() ?? this.deviceIdentity.deviceId);

  readonly formFactorIcons = FORM_FACTOR_ICONS;
  readonly clientTypeLabelKeys = CLIENT_TYPE_LABEL_KEYS;

  isProvidedDevice(device: Device): boolean {
    return !!device.providerId;
  }

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly devices = computed(() =>
    [...this.deviceService.devices()].sort((a, b) => this.compareDevices(a, b)));

  readonly openMenuDeviceId = signal<string | null>(null);

  readonly renamingDevice = signal<Device | null>(null);
  readonly renameValue = signal('');
  readonly logoutCandidate = signal<Device | null>(null);
  readonly removeCandidate = signal<Device | null>(null);

  readonly startupProfileDevice = signal<Device | null>(null);
  readonly startupProfileDraft = signal('');

  readonly openProfileDevice = signal<Device | null>(null);
  readonly openProfileDraft = signal('');

  readonly profileOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.localization.translateKey(AppStrings.Settings.Devices.DefaultFirstProfileOption) },
    ...this.profileService.sortedProfiles().map(p => ({ value: p.id, label: p.name })),
  ]);

  async ngOnInit(): Promise<void> {
    await this.deviceService.load();
  }

  isThisDevice(device: Device): boolean {
    const id = this.thisDeviceId();
    return id !== null && device.id === id;
  }

  sessionState(device: Device): DeviceSessionState {
    if (device.online) {
      return 'online';
    }
    return device.hasActiveSession ? 'offline' : 'signedOut';
  }

  statusLabel(device: Device): string {
    const state = this.sessionState(device);
    if (state === 'online') {
      return device.connectionCount > 1
        ? this.localization.translateKey(AppStrings.Settings.Devices.OnlineStatusWithConnections, {
          count: device.connectionCount,
        })
        : this.localization.translateKey(AppStrings.Settings.Devices.OnlineStatus);
    }
    return this.localization.translateKey(
      state === 'signedOut' ? AppStrings.Settings.Devices.SignedOutMarker : AppStrings.Settings.Devices.OfflineStatus,
    );
  }

  setMenuOpen(deviceId: string, isOpen: boolean): void {
    this.openMenuDeviceId.set(isOpen ? deviceId : null);
  }

  startRename(device: Device): void {
    this.openMenuDeviceId.set(null);
    this.renamingDevice.set(device);
    this.renameValue.set(device.name);
  }

  cancelRename(): void {
    dismissModal(this.modal, () => {
      this.renamingDevice.set(null);
      this.renameValue.set('');
    });
  }

  async commitRename(): Promise<void> {
    const device = this.renamingDevice();
    const name = this.renameValue().trim();
    if (!device || !name) {
      return;
    }
    await this.deviceService.rename(device.id, name);
    this.cancelRename();
  }

  startupProfileLabel(device: Device): string {
    if (!device.startupProfileId) {
      return this.localization.translateKey(AppStrings.Settings.Devices.StartupProfileDefault);
    }
    return device.startupProfileName ?? this.localization.translateKey(AppStrings.Settings.Devices.StartupProfileUnavailable);
  }

  isStartupProfileUnavailable(device: Device): boolean {
    return !!device.startupProfileId && !device.startupProfileName;
  }

  startStartupProfile(device: Device): void {
    this.openMenuDeviceId.set(null);
    this.startupProfileDevice.set(device);
    this.startupProfileDraft.set(device.startupProfileId ?? '');
  }

  cancelStartupProfile(): void {
    dismissModal(this.modal, () => {
      this.startupProfileDevice.set(null);
      this.startupProfileDraft.set('');
    });
  }

  async commitStartupProfile(): Promise<void> {
    const device = this.startupProfileDevice();
    if (!device) {
      return;
    }
    const response = await this.deviceService.setStartupProfile(device.id, this.startupProfileDraft() || null);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Settings.Devices.StartupProfileSaveFailed),
        { variant: 'error' },
      );
    }
    this.cancelStartupProfile();
  }

  startOpenProfile(device: Device): void {
    if (!device.online) {
      return;
    }
    this.openMenuDeviceId.set(null);
    this.openProfileDevice.set(device);
    // A kept-but-unresolvable assignment is not in the picker; fall back to Default rather than
    // seeding a value the select cannot show and the host would reject.
    const assigned = device.startupProfileId;
    const offered = this.profileService.sortedProfiles().some(profile => profile.id === assigned);
    this.openProfileDraft.set(assigned && offered ? assigned : '');
  }

  cancelOpenProfile(): void {
    dismissModal(this.modal, () => {
      this.openProfileDevice.set(null);
      this.openProfileDraft.set('');
    });
  }

  async commitOpenProfile(): Promise<void> {
    const device = this.openProfileDevice();
    if (!device) {
      return;
    }
    const profileId = this.openProfileDraft() || this.profileService.sortedProfiles()[0]?.id;
    if (!profileId) {
      return;
    }
    const response = await this.deviceService.openProfile(device.id, profileId);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Settings.Devices.OpenProfileFailed),
        { variant: 'error' },
      );
    }
    this.cancelOpenProfile();
  }

  requestLogout(device: Device): void {
    if (this.isThisDevice(device)) {
      return;
    }
    this.openMenuDeviceId.set(null);
    this.logoutCandidate.set(device);
  }

  cancelLogout(): void {
    this.logoutCandidate.set(null);
  }

  async confirmLogout(): Promise<void> {
    const device = this.logoutCandidate();
    if (!device) {
      return;
    }
    await this.deviceService.logout(device.id);
    this.logoutCandidate.set(null);
  }

  requestRemove(device: Device): void {
    this.openMenuDeviceId.set(null);
    this.removeCandidate.set(device);
  }

  cancelRemove(): void {
    this.removeCandidate.set(null);
  }

  async confirmRemove(): Promise<void> {
    const device = this.removeCandidate();
    if (!device) {
      return;
    }
    const isCurrentDevice = this.isThisDevice(device);
    try {
      const response = await this.deviceService.remove(device.id);
      if (!response.success) {
        this.toastService.show(
          response.error?.message ?? this.localization.translateKey(AppStrings.Settings.Devices.RemoveFailed),
          { variant: 'error' },
        );
        return;
      }
      this.removeCandidate.set(null);
      if (isCurrentDevice) {
        await this.authService.logout();
      }
    } catch {
      this.toastService.show(this.localization.translateKey(AppStrings.Settings.Devices.RemoveFailed), { variant: 'error' });
    }
  }

  secondaryLine(device: Device): string {
    const parts = [device.browser, device.platform].filter((part): part is string => !!part);
    parts.push(this.localization.translateKey(AppStrings.Settings.Devices.LastSeenPrefix, {
      time: this.relativeTime(device.lastSeenAt),
    }));
    return parts.join(' · ');
  }

  private compareDevices(a: Device, b: Device): number {
    if (this.isThisDevice(a)) return -1;
    if (this.isThisDevice(b)) return 1;
    if (a.online !== b.online) return a.online ? -1 : 1;
    return b.lastSeenAt.localeCompare(a.lastSeenAt);
  }

  private relativeTime(iso: string): string {
    if (Number.isNaN(new Date(iso).getTime())) {
      return this.localization.translateKey(AppStrings.Settings.Devices.LastSeenUnknown);
    }
    return formatRelativeTime(iso, (key, args) => this.localization.translateKey(key, args));
  }
}

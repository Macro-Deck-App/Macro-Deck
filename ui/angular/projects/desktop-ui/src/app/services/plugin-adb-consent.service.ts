import { Injectable, computed, inject, signal } from '@angular/core';
import { AppStrings, UserNotification } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { NotificationCenterService } from './notification-center.service';
import { SettingsModalService } from './settings-modal.service';

@Injectable({ providedIn: 'root' })
export class PluginAdbConsentService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly notificationCenter = inject(NotificationCenterService);
  private readonly settingsModal = inject(SettingsModalService);

  private readonly deferred = signal<ReadonlySet<string>>(new Set());

  readonly pending = computed<UserNotification | null>(() => {
    const deferred = this.deferred();
    return this.notificationCenter.notifications().find(notification =>
      !deferred.has(notification.id) && isAdbConsentRequest(notification)) ?? null;
  });

  readonly offeringDownload = signal(false);
  readonly downloading = signal(false);
  readonly downloadError = signal<string | null>(null);

  async allow(notificationId: string): Promise<boolean> {
    const response = await this.api.updateAdbSettings({ enabled: true, allowPlugins: true })
      .catch(() => null);
    if (!response?.success) {
      this.defer(notificationId);
      this.settingsModal.open('adb');
      return false;
    }
    this.notificationCenter.dismiss(notificationId);
    if (!response.resolvedExecutablePath) {
      this.downloadError.set(null);
      this.offeringDownload.set(true);
    }
    return true;
  }

  defer(notificationId: string): void {
    this.deferred.update(deferred => new Set(deferred).add(notificationId));
  }

  async downloadPlatformTools(): Promise<void> {
    if (this.downloading()) {
      return;
    }
    this.downloadError.set(null);
    this.downloading.set(true);
    try {
      const response = await this.api.downloadAdbPlatformTools();
      if (response.success) {
        this.offeringDownload.set(false);
      } else {
        this.downloadError.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Adb.DownloadFailed));
      }
    } catch {
      this.downloadError.set(this.localization.translateKey(AppStrings.Settings.Adb.DownloadFailed));
    } finally {
      this.downloading.set(false);
    }
  }

  skipDownload(): void {
    if (!this.downloading()) {
      this.offeringDownload.set(false);
    }
  }
}

function isAdbConsentRequest(notification: UserNotification): boolean {
  return (notification.actions ?? []).some(action => action.kind === 'EnablePluginAdb')
    || notification.action?.kind === 'EnablePluginAdb';
}

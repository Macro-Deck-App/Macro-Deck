import { Injectable, effect, inject, signal } from '@angular/core';
import { AppStrings, Device, DeviceChangedEvent, DeviceRemovedEvent, OpenProfileOnDeviceResponse, RemoveDeviceResponse, SetDeviceStartupProfileResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({ providedIn: 'root' })
export class DeviceService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly devices = signal<Device[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private inFlightLoad: Promise<void> | null = null;

  private loadOverlay: Map<string, Device | null> | null = null;

  constructor() {
    this.subscribeToEvents();

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.load();
      }
    });
  }

  load(): Promise<void> {
    this.inFlightLoad ??= this.runLoad().finally(() => {
      this.inFlightLoad = null;
    });
    return this.inFlightLoad;
  }

  private async runLoad(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    const overlay = new Map<string, Device | null>();
    this.loadOverlay = overlay;
    try {
      const response = await this.api.getDevices();
      const snapshot = response.devices ?? [];
      this.devices.set(overlay.size === 0 ? snapshot : applyOverlay(snapshot, overlay));
    } catch (error) {
      console.error('Failed to load devices:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Device.LoadFailed));
    } finally {
      this.loadOverlay = null;
      this.isLoading.set(false);
    }
  }

  async rename(id: string, name: string): Promise<Device | null> {
    const response = await this.api.renameDevice(id, { name });
    if (!response.success || !response.device) {
      console.warn('renameDevice failed:', response.error);
      return null;
    }
    this.upsertLocal(response.device);
    return response.device;
  }

  async setStartupProfile(id: string, profileId: string | null): Promise<SetDeviceStartupProfileResponse> {
    const response = await this.api.setDeviceStartupProfile(id, { profileId });
    if (response.success && response.device) {
      this.upsertLocal(response.device);
    }
    return response;
  }

  async logout(id: string): Promise<boolean> {
    const response = await this.api.logoutDevice(id);
    if (!response.success) {
      console.warn('logoutDevice failed:', response.error);
      return false;
    }
    return true;
  }

  async remove(id: string): Promise<RemoveDeviceResponse> {
    const response = await this.api.removeDevice(id);
    if (response.success) {
      this.deleteLocal(id);
    }
    return response;
  }

  openProfile(id: string, profileId: string): Promise<OpenProfileOnDeviceResponse> {
    return this.api.openProfileOnDevice(id, { profileId });
  }

  private subscribeToEvents(): void {
    this.api.onNotification<DeviceChangedEvent>('DeviceChangedEvent').subscribe(evt => {
      if (evt.device) this.upsertLocal(evt.device);
    });

    this.api.onNotification<DeviceRemovedEvent>('DeviceRemovedEvent').subscribe(evt => {
      if (evt.deviceId) this.deleteLocal(evt.deviceId);
    });
  }

  private upsertLocal(device: Device): void {
    this.loadOverlay?.set(device.id, device);
    this.devices.update(list => {
      const idx = list.findIndex(d => d.id === device.id);
      if (idx === -1) return [...list, device];
      const next = list.slice();
      next[idx] = device;
      return next;
    });
  }

  private deleteLocal(id: string): void {
    this.loadOverlay?.set(id, null);
    this.devices.update(list => list.filter(d => d.id !== id));
  }
}

function applyOverlay(snapshot: Device[], overlay: Map<string, Device | null>): Device[] {
  const merged = snapshot.filter(d => overlay.get(d.id) !== null);
  for (const [id, device] of overlay) {
    if (device === null) continue;
    const idx = merged.findIndex(d => d.id === id);
    if (idx === -1) merged.push(device);
    else merged[idx] = device;
  }
  return merged;
}

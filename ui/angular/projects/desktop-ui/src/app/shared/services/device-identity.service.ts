import { Injectable, InjectionToken, inject } from '@angular/core';
import { collectUserAgentInfo, DeviceClientType, DeviceCredential, DeviceLoginInfo } from '@macro-deck/runtime';

export const CLIENT_TYPE = new InjectionToken<DeviceClientType>('CLIENT_TYPE', {
  providedIn: 'root',
  factory: () => 'unknown',
});

interface StoredCredential {
  deviceId: string | null;
  deviceSecret: string | null;
}

@Injectable({ providedIn: 'root' })
export class DeviceIdentityService {
  private readonly clientType = inject(CLIENT_TYPE);
  private readonly storageKeyId = `md.device.${this.clientType}.id`;
  private readonly storageKeySecret = `md.device.${this.clientType}.secret`;

  private credential: StoredCredential;

  constructor() {
    this.credential = this.readStored();
  }

  get deviceId(): string | null {
    return this.credential.deviceId;
  }

  buildLoginInfo(): DeviceLoginInfo {
    const info = collectUserAgentInfo();
    return {
      deviceId: this.credential.deviceId ?? undefined,
      deviceSecret: this.credential.deviceSecret ?? undefined,
      clientType: this.clientType,
      proposedName: info.proposedName,
      platform: info.platform ?? undefined,
      browser: info.browser ?? undefined,
      formFactor: info.formFactor,
      // No build version in the Angular workspace (package.json is 0.0.0) - reserved for a future native app.
      appVersion: undefined,
    };
  }

  adopt(credential: DeviceCredential | undefined): void {
    if (!credential?.deviceId) {
      return;
    }
    this.credential = {
      deviceId: credential.deviceId,
      deviceSecret: credential.deviceSecret ?? this.credential.deviceSecret,
    };
    this.writeItem(this.storageKeyId, credential.deviceId);
    if (credential.deviceSecret) {
      this.writeItem(this.storageKeySecret, credential.deviceSecret);
    }
  }

  private readStored(): StoredCredential {
    try {
      return {
        deviceId: localStorage.getItem(this.storageKeyId),
        deviceSecret: localStorage.getItem(this.storageKeySecret),
      };
    } catch {
      return { deviceId: null, deviceSecret: null };
    }
  }

  private writeItem(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
    } catch {
    }
  }
}

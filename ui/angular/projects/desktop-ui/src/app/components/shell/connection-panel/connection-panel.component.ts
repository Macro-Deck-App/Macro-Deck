import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Output,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { toDataURL } from 'qrcode';

import { AppStrings, ConnectionEndpoint, GetConnectionInfoResponse, PairingCodeResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe, LocalizationKey } from '@shared';
import { ExternalLinkService } from '../../../services/external-link.service';

interface AddressGroup {
  address: string;
  endpoints: ConnectionEndpoint[];
}

const PAIRING_POLL_SECONDS = 5;

@Component({
  selector: 'app-connection-panel',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './connection-panel.component.html',
  styleUrls: ['./connection-panel.component.scss'],
})
export class ConnectionPanelComponent {
  private readonly api = inject(ApiService);
  private readonly externalLinks = inject(ExternalLinkService);
  protected readonly localization = inject(LocalizationService);
  protected readonly appStrings = AppStrings;

  readonly isOpen = input(false);

  @Output() closed = new EventEmitter<void>();

  protected readonly info = signal<GetConnectionInfoResponse | null>(null);
  protected readonly qrDataUrl = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly pairingCode = signal<PairingCodeResponse | null>(null);
  private readonly now = signal(Date.now());
  private timer: ReturnType<typeof setInterval> | null = null;
  private loadGeneration = 0;

  protected readonly expanded = signal<string | null>(null);

  protected readonly groupedPairingCode = computed(() => {
    const code = this.pairingCode()?.code;
    return code ? `${code.slice(0, 3)} ${code.slice(3)}` : null;
  });

  protected readonly pairingCodeRemaining = computed(() => {
    const expiresAt = this.pairingCode()?.expiresAt;
    const seconds = expiresAt ? Math.max(0, Math.ceil((Date.parse(expiresAt) - this.now()) / 1000)) : 0;
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  protected readonly addressGroups = computed<AddressGroup[]>(() => {
    const groups: AddressGroup[] = [];
    for (const endpoint of this.info()?.endpoints ?? []) {
      const existing = groups.find((group) => group.address === endpoint.address);
      if (existing) {
        existing.endpoints.push(endpoint);
      } else {
        groups.push({ address: endpoint.address, endpoints: [endpoint] });
      }
    }
    return groups;
  });

  protected readonly anySsl = computed(() => (this.info()?.endpoints ?? []).some((endpoint) => endpoint.ssl));

  constructor() {
    effect(() => {
      if (this.isOpen()) {
        void this.load();
      } else {
        this.stopPairingTimer();
      }
    });
    inject(DestroyRef).onDestroy(() => this.stopPairingTimer());
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.expanded.set(null);
    this.stopPairingTimer();
    const generation = ++this.loadGeneration;

    try {
      const info = await this.api.getConnectionInfo();
      const code = await this.api.rotatePairingCode().catch(() => null);
      if (generation !== this.loadGeneration) {
        return;
      }

      this.info.set(info);
      await this.showPairingCode(code);
      if (code && this.isOpen()) {
        this.startPairingTimer();
      }
    } catch {
      if (generation === this.loadGeneration) {
        this.info.set(null);
      }
    } finally {
      if (generation === this.loadGeneration) {
        this.loading.set(false);
      }
    }
  }

  private startPairingTimer(): void {
    this.stopPairingTimer();
    let ticks = 0;
    this.timer = setInterval(() => {
      this.now.set(Date.now());
      const expired = Date.parse(this.pairingCode()?.expiresAt ?? '') <= Date.now();
      if (++ticks % PAIRING_POLL_SECONDS === 0 || expired) {
        void this.pollPairingCode();
      }
    }, 1000);
  }

  private stopPairingTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  private async pollPairingCode(): Promise<void> {
    const generation = this.loadGeneration;
    try {
      const code = await this.api.getPairingCode();
      if (generation !== this.loadGeneration) {
        return;
      }

      if (code.code !== this.pairingCode()?.code || code.expiresAt !== this.pairingCode()?.expiresAt) {
        await this.showPairingCode(code);
      }
    } catch {
      if (generation === this.loadGeneration) {
        this.stopPairingTimer();
        await this.showPairingCode(null);
      }
    }
  }

  private async showPairingCode(code: PairingCodeResponse | null): Promise<void> {
    this.now.set(Date.now());
    this.pairingCode.set(code);
    const info = this.info();
    this.qrDataUrl.set(info ? await toDataURL(this.buildConnectUrl(info, code?.code ?? ''), { margin: 1, width: 220, errorCorrectionLevel: 'L' }) : null);
  }

  protected key(endpoint: ConnectionEndpoint): string {
    return `${endpoint.ssl ? 'https' : 'http'}://${endpoint.address}:${endpoint.port}`;
  }

  protected sslLabelKey(endpoint: ConnectionEndpoint): LocalizationKey {
    return endpoint.ssl ? AppStrings.Shell.ConnectionPanel.EncryptedConnection
      : AppStrings.Shell.ConnectionPanel.UnencryptedConnection;
  }

  toggle(endpoint: ConnectionEndpoint): void {
    const key = this.key(endpoint);
    this.expanded.update((current) => (current === key ? null : key));
  }

  open(endpoint: ConnectionEndpoint, target: 'config' | 'client'): void {
    const suffix = target === 'client' ? '' : '/admin';
    this.externalLinks.open(`${this.key(endpoint)}${suffix}`);
    this.expanded.set(null);
  }

  private buildConnectUrl(info: GetConnectionInfoResponse, token: string): string {
    const payload = {
      payloadVersion: 2,
      instanceName: info.instanceName,
      endpoints: info.endpoints,
      token,
      version: info.version,
    };
    const base64 = btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(payload))));
    return `https://connect.macro-deck.app/${base64}`;
  }
}

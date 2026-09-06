import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, effect, inject, input, signal } from '@angular/core';
import { toDataURL } from 'qrcode';

import { AppStrings, ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe, LocalizationKey } from '@shared';
import { ExternalLinkService } from '../../../services/external-link.service';

interface AddressGroup {
  address: string;
  endpoints: ConnectionEndpoint[];
}

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

  protected readonly expanded = signal<string | null>(null);

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
      }
    });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.expanded.set(null);

    try {
      const info = await this.api.getConnectionInfo();
      this.info.set(info);
      this.qrDataUrl.set(await toDataURL(this.buildConnectUrl(info), { margin: 1, width: 220 }));
    } catch {
      this.info.set(null);
    } finally {
      this.loading.set(false);
    }
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

  private buildConnectUrl(info: GetConnectionInfoResponse): string {
    const payload = {
      payloadVersion: 2,
      instanceName: info.instanceName,
      endpoints: info.endpoints,
      token: '',
      version: info.version,
    };
    const base64 = btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(payload))));
    return `https://connect.macro-deck.app/${base64}`;
  }
}

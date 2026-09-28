import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';

import { AppStrings, ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, SidePanelComponent, TranslatePipe, LocalizationKey } from '@shared';
import { ExternalLinkService } from '../../../services/external-link.service';
import { ConnectQrComponent } from '../connect-qr/connect-qr.component';

interface AddressGroup {
  address: string;
  endpoints: ConnectionEndpoint[];
}

@Component({
  selector: 'app-connection-panel',
  standalone: true,
  imports: [SidePanelComponent, TranslatePipe, ConnectQrComponent],
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
  protected readonly loading = signal(true);
  private loadGeneration = 0;

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
    const generation = ++this.loadGeneration;

    try {
      const info = await this.api.getConnectionInfo();
      if (generation === this.loadGeneration) {
        this.info.set(info);
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
}

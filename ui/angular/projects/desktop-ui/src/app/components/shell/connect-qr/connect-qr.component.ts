import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toDataURL } from 'qrcode';

import { AppStrings, GetConnectionInfoResponse, PairingCodeResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationKey, TranslatePipe } from '@shared';
import { encodeConnectLink } from './connect-link';

const PAIRING_POLL_SECONDS = 5;

// Mints a fresh pairing code for every info it is given, so each place that shows it replaces the
// code another place showed before.
@Component({
  selector: 'app-connect-qr',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './connect-qr.component.html',
  styleUrls: ['./connect-qr.component.scss'],
})
export class ConnectQrComponent {
  private readonly api = inject(ApiService);
  protected readonly appStrings = AppStrings;

  readonly info = input.required<GetConnectionInfoResponse>();
  readonly hint = input<LocalizationKey | null>(AppStrings.Shell.ConnectionPanel.ScanHint);

  protected readonly qrDataUrl = signal<string | null>(null);
  protected readonly pairingCode = signal<PairingCodeResponse | null>(null);
  private readonly now = signal(Date.now());
  private timer: ReturnType<typeof setInterval> | null = null;
  private generation = 0;

  protected readonly groupedPairingCode = computed(() => {
    const code = this.pairingCode()?.code;
    return code ? `${code.slice(0, 3)} ${code.slice(3)}` : null;
  });

  protected readonly pairingCodeRemaining = computed(() => {
    const expiresAt = this.pairingCode()?.expiresAt;
    const seconds = expiresAt ? Math.max(0, Math.ceil((Date.parse(expiresAt) - this.now()) / 1000)) : 0;
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  constructor() {
    effect(() => {
      const info = this.info();
      untracked(() => void this.start(info));
    });
    inject(DestroyRef).onDestroy(() => {
      this.generation++;
      this.stopPairingTimer();
    });
  }

  private async start(info: GetConnectionInfoResponse): Promise<void> {
    this.stopPairingTimer();
    const generation = ++this.generation;
    const code = await this.api.rotatePairingCode().catch(() => null);
    if (generation !== this.generation) {
      return;
    }

    await this.showPairingCode(info, code);
    if (code && generation === this.generation) {
      this.startPairingTimer();
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
    const generation = this.generation;
    try {
      const code = await this.api.getPairingCode();
      if (generation !== this.generation) {
        return;
      }

      if (code.code !== this.pairingCode()?.code || code.expiresAt !== this.pairingCode()?.expiresAt) {
        await this.showPairingCode(this.info(), code);
      }
    } catch {
      if (generation === this.generation) {
        this.stopPairingTimer();
        await this.showPairingCode(this.info(), null);
      }
    }
  }

  private async showPairingCode(info: GetConnectionInfoResponse, code: PairingCodeResponse | null): Promise<void> {
    this.now.set(Date.now());
    this.pairingCode.set(code);
    this.qrDataUrl.set(await toDataURL(this.buildConnectUrl(info, code?.code ?? ''), { margin: 1, width: 220, errorCorrectionLevel: 'L' }));
  }

  private buildConnectUrl(info: GetConnectionInfoResponse, token: string): string {
    return encodeConnectLink(info, token);
  }
}

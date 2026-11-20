import { Injectable, signal } from '@angular/core';

export type ConnectionStep = 'key-ring' | 'auth' | 'host-url' | 'ticket' | 'socket' | 'register';

export interface ConnectionReport {
  step: ConnectionStep;
  reason: string;
  attempt: number;
  recovered: boolean;
}

const STALLED_AFTER_MS = 15_000;
const REPEAT_REPORT_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class ConnectionDiagnosticsService {
  readonly stalled = signal(false);

  private startedAt: number | null = null;
  private connected = false;
  private active = true;
  private step: ConnectionStep = 'key-ring';
  private failures = 0;
  private lastReported: { step: ConnectionStep; reason: string; at: number } | null = null;
  private stallTimer: ReturnType<typeof setTimeout> | null = null;

  attempting(step: ConnectionStep): void {
    if (this.connected || !this.active) {
      return;
    }
    this.step = step;
    if (this.startedAt === null) {
      this.startedAt = Date.now();
      this.stallTimer = setTimeout(() => this.markStalled(), STALLED_AFTER_MS);
    }
  }

  fail(step: ConnectionStep, error: unknown): void {
    if (this.connected || !this.active) {
      return;
    }
    this.attempting(step);
    this.failures++;
    const reason = describe(error);
    const last = this.lastReported;
    if (last === null || last.step !== step || last.reason !== reason || Date.now() - last.at >= REPEAT_REPORT_MS) {
      this.report({ step, reason, attempt: this.failures, recovered: false });
    }
  }

  connectedNow(): void {
    if (this.connected) {
      return;
    }
    this.connected = true;
    this.clearStallTimer();
    this.stalled.set(false);
    if (this.lastReported !== null) {
      this.report({ step: this.step, reason: '', attempt: this.failures, recovered: true });
    }
  }

  setActive(active: boolean): void {
    this.active = active;
    if (!active) {
      this.clearStallTimer();
      this.startedAt = null;
      this.stalled.set(false);
    }
  }

  private markStalled(): void {
    this.stallTimer = null;
    if (this.connected) {
      return;
    }
    this.stalled.set(true);
    if (this.lastReported === null) {
      this.report({ step: this.step, reason: 'no answer yet', attempt: this.failures, recovered: false });
    }
  }

  private report(report: ConnectionReport): void {
    this.lastReported = { step: report.step, reason: report.reason, at: Date.now() };
    const bridge = window.macroDeckShell;
    if (typeof bridge?.reportUiConnection === 'function') {
      bridge.reportUiConnection(report).catch(() => {
      });
    }
  }

  private clearStallTimer(): void {
    if (this.stallTimer !== null) {
      clearTimeout(this.stallTimer);
      this.stallTimer = null;
    }
  }
}

function describe(error: unknown): string {
  if (error instanceof Error) {
    return error.message || error.name;
  }
  return String(error);
}

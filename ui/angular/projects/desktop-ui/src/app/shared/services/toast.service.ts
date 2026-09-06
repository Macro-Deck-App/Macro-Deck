import { Injectable, signal } from '@angular/core';

export type ToastVariant = 'success' | 'error';

export interface Toast {
  id: number;
  message: string;
  detail?: string;
  variant: ToastVariant;
}

export interface ToastOptions {
  detail?: string;
  variant?: ToastVariant;
  durationMs?: number;
}

const DEFAULT_DURATION_MS = 5000;

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);

  private nextId = 1;
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  show(message: string, options: ToastOptions = {}): number {
    const id = this.nextId++;
    const toast: Toast = {
      id,
      message,
      detail: options.detail,
      variant: options.variant ?? 'success',
    };
    this.toasts.update(toasts => [...toasts, toast]);

    const duration = options.durationMs ?? DEFAULT_DURATION_MS;
    if (duration > 0) {
      this.timers.set(id, setTimeout(() => this.dismiss(id), duration));
    }
    return id;
  }

  dismiss(id: number): void {
    const timer = this.timers.get(id);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.timers.delete(id);
    }
    this.toasts.update(toasts => toasts.filter(toast => toast.id !== id));
  }
}

import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';

import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';
import { ToastService } from './toast.service';

export const MAX_PALETTE_COLORS = 24;

// The host's own defaults, shown until the host answers so a popover is never empty while connecting.
const DEFAULT_PALETTE_COLORS = ['#ef4444', '#f59e0b', '#eab308', '#22c55e', '#3b82f6', '#8b5cf6', '#ec4899'];

@Injectable({ providedIn: 'root' })
export class ColorPaletteService {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  private readonly palette = signal<readonly string[]>(DEFAULT_PALETTE_COLORS);
  private generation = 0;

  readonly colors = this.palette.asReadonly();
  readonly isFull = computed(() => this.palette().length >= MAX_PALETTE_COLORS);

  constructor() {
    this.api.onColorPaletteChanged().subscribe(event => this.apply(event.colors ?? []));

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        untracked(() => void this.load());
      }
    });
  }

  contains(color: string): boolean {
    return this.palette().includes(color.toLowerCase());
  }

  add(color: string): Promise<void> {
    return this.set(color.toLowerCase(), true);
  }

  remove(color: string): Promise<void> {
    return this.set(color.toLowerCase(), false);
  }

  async restoreDefaults(): Promise<void> {
    const startedAt = this.generation;
    try {
      const response = await this.api.restoreDefaultColorPalette();
      if (response.success && startedAt === this.generation) {
        this.apply(response.colors ?? []);
      } else if (!response.success) {
        this.reportFailure(response.error?.message);
      }
    } catch (error) {
      console.error('Failed to restore the default color palette:', error);
      this.reportFailure();
    }
  }

  private async set(color: string, present: boolean): Promise<void> {
    if (this.contains(color) === present) return;
    const before = this.palette();
    this.setLocally(present ? [...before, color] : before.filter(entry => entry !== color));
    const startedAt = this.generation;

    try {
      const response = await this.api.setColorPaletteEntry(color, present);
      const newest = startedAt === this.generation;
      if (response.success) {
        if (newest) this.apply(response.colors ?? []);
        return;
      }
      if (newest) this.apply(response.colors ?? before);
      this.reportFailure(response.error?.message);
    } catch (error) {
      console.error('Failed to save the color palette:', error);
      if (startedAt === this.generation) this.setLocally(before);
      this.reportFailure();
    }
  }

  private async load(): Promise<void> {
    const startedAt = this.generation;
    try {
      const response = await this.api.getColorPalette();
      if (response.success && startedAt === this.generation) {
        this.apply(response.colors ?? []);
      }
    } catch (error) {
      console.error('Failed to load the color palette:', error);
    }
  }

  private apply(colors: readonly string[]): void {
    this.generation++;
    this.palette.set([...colors]);
  }

  private setLocally(colors: readonly string[]): void {
    this.generation++;
    this.palette.set(colors);
  }

  private reportFailure(detail?: string): void {
    this.toasts.show(this.localization.translateKey(AppStrings.Forms.ColorPicker.PaletteSaveFailed), {
      variant: 'error',
      detail: resolveLocalizedText(detail, this.localization) || undefined,
    });
  }
}

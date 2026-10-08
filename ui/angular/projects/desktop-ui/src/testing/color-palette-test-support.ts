import { Provider, computed, signal } from '@angular/core';
import { ColorPaletteService, MAX_PALETTE_COLORS } from '@shared';

export class FakeColorPalette {
  readonly colors = signal<readonly string[]>([]);
  readonly isFull = computed(() => this.colors().length >= MAX_PALETTE_COLORS);
  readonly added: string[] = [];
  readonly removed: string[] = [];
  restoredDefaults = 0;

  contains(color: string): boolean {
    return this.colors().includes(color.toLowerCase());
  }

  add(color: string): Promise<void> {
    this.added.push(color);
    this.colors.update(current => [...current, color.toLowerCase()]);
    return Promise.resolve();
  }

  restoreDefaults(): Promise<void> {
    this.restoredDefaults++;
    return Promise.resolve();
  }

  remove(color: string): Promise<void> {
    this.removed.push(color);
    this.colors.update(current => current.filter(entry => entry !== color.toLowerCase()));
    return Promise.resolve();
  }
}

export function provideColorPaletteTesting(palette = new FakeColorPalette()): Provider[] {
  return [{ provide: ColorPaletteService, useValue: palette }];
}

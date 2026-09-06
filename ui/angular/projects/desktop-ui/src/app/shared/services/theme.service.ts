import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { type AppearanceChangedEvent, type ThemeMode } from '@macro-deck/runtime';
import { ApiService } from '../transport';

export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY_MODE = 'md.appearance.themeMode';
const STORAGE_KEY_ACCENT = 'md.appearance.accentColor';

export const DEFAULT_THEME_MODE: ThemeMode = 'system';
export const DEFAULT_ACCENT_COLOR = '#2196F3';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly api = inject(ApiService);

  readonly themeMode = signal<ThemeMode>(DEFAULT_THEME_MODE);
  readonly accentColor = signal<string>(DEFAULT_ACCENT_COLOR);

  private readonly systemPrefersDark = signal<boolean>(true);

  readonly resolvedTheme = computed<ResolvedTheme>(() => {
    const mode = this.themeMode();
    if (mode === 'system') {
      return this.systemPrefersDark() ? 'dark' : 'light';
    }
    return mode;
  });

  constructor() {
    this.restoreFromCache();
    this.watchSystemPreference();

    effect(() => {
      const theme = this.resolvedTheme();
      const accent = this.accentColor();
      this.applyToDom(theme, accent);
      localStorage.setItem(STORAGE_KEY_MODE, this.themeMode());
      localStorage.setItem(STORAGE_KEY_ACCENT, accent);
    });

    this.api
      .onNotification<AppearanceChangedEvent>('AppearanceChangedEvent')
      .subscribe(evt => this.applyServerSettings(evt.themeMode, evt.accentColor));

    // The one load during startup can land before there is a session to answer it, and it fails
    // quietly when it does - leaving the deck on its cached colours for the rest of the visit. Every
    // arrival at a live connection re-asks, which also picks up a change pushed while it was down.
    effect(() => {
      if (this.api.connectionStateSignal() !== 'connected') return;
      void this.loadFromHost();
    });
  }

  async loadFromHost(): Promise<void> {
    try {
      const settings = await this.api.getAppearanceSettings();
      this.applyServerSettings(settings.themeMode, settings.accentColor);
    } catch {
    }
  }

  private applyServerSettings(themeMode: ThemeMode | undefined, accentColor: string | undefined): void {
    this.themeMode.set(themeMode ?? DEFAULT_THEME_MODE);
    this.accentColor.set(accentColor ?? DEFAULT_ACCENT_COLOR);
  }

  setThemeMode(mode: ThemeMode): void {
    this.themeMode.set(mode);
    void this.persist();
  }

  setAccentColor(color: string): void {
    this.accentColor.set(color);
    void this.persist();
  }

  private async persist(): Promise<void> {
    try {
      await this.api.updateAppearanceSettings({
        themeMode: this.themeMode(),
        accentColor: this.accentColor()
      });
    } catch {
    }
  }

  private restoreFromCache(): void {
    const cachedMode = localStorage.getItem(STORAGE_KEY_MODE) as ThemeMode | null;
    const cachedAccent = localStorage.getItem(STORAGE_KEY_ACCENT);
    if (cachedMode === 'light' || cachedMode === 'dark' || cachedMode === 'system') {
      this.themeMode.set(cachedMode);
    }
    if (cachedAccent) {
      this.accentColor.set(cachedAccent);
    }
  }

  private watchSystemPreference(): void {
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    this.systemPrefersDark.set(query.matches);
    const onChange = (event: MediaQueryListEvent) => this.systemPrefersDark.set(event.matches);
    if (typeof query.addEventListener === 'function') {
      query.addEventListener('change', onChange);
    } else {
      query.addListener(onChange);
    }
  }

  private applyToDom(theme: ResolvedTheme, accent: string): void {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(theme);
    root.style.setProperty('--color-accent', accent);
    root.style.setProperty('--color-accent-hover', darken(accent, 0.2));
    root.style.setProperty('--color-accent-muted', withAlpha(accent, 0.15));
  }
}

interface Rgb {
  r: number;
  g: number;
  b: number;
}

function parseHex(hex: string): Rgb | null {
  const value = hex.trim().replace('#', '');
  if (value.length === 3) {
    return {
      r: parseInt(value[0] + value[0], 16),
      g: parseInt(value[1] + value[1], 16),
      b: parseInt(value[2] + value[2], 16)
    };
  }
  if (value.length === 6) {
    return {
      r: parseInt(value.slice(0, 2), 16),
      g: parseInt(value.slice(2, 4), 16),
      b: parseInt(value.slice(4, 6), 16)
    };
  }
  return null;
}

function toHex(channel: number): string {
  return Math.max(0, Math.min(255, Math.round(channel))).toString(16).padStart(2, '0');
}

function darken(hex: string, amount: number): string {
  const rgb = parseHex(hex);
  if (!rgb) {
    return hex;
  }
  const factor = 1 - amount;
  return `#${toHex(rgb.r * factor)}${toHex(rgb.g * factor)}${toHex(rgb.b * factor)}`;
}

function withAlpha(hex: string, alpha: number): string {
  const rgb = parseHex(hex);
  if (!rgb) {
    return hex;
  }
  return `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${alpha})`;
}

import { supportsCustomProperties, type SystemFontFace, ThemeMode, type UiFont } from '@macro-deck/runtime';
import { applyAccentStyles } from './accent-styles';

export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY_MODE = 'md.appearance.themeMode';
const STORAGE_KEY_ACCENT = 'md.appearance.accentColor';
const STORAGE_KEY_FONT = 'md.appearance.fontFamily';

export const DEFAULT_THEME_MODE: ThemeMode = 'system';
export const DEFAULT_ACCENT_COLOR = '#2196F3';

interface Rgb {
  r: number;
  g: number;
  b: number;
}

function parseHex(hex: string): Rgb | null {
  const value = hex.replace(/^\s+|\s+$/g, '').replace('#', '');
  if (value.length === 3) {
    return {
      r: parseInt(value.charAt(0) + value.charAt(0), 16),
      g: parseInt(value.charAt(1) + value.charAt(1), 16),
      b: parseInt(value.charAt(2) + value.charAt(2), 16),
    };
  }
  if (value.length === 6) {
    return {
      r: parseInt(value.slice(0, 2), 16),
      g: parseInt(value.slice(2, 4), 16),
      b: parseInt(value.slice(4, 6), 16),
    };
  }
  return null;
}

function toHex(channel: number): string {
  const clamped = Math.max(0, Math.min(255, Math.round(channel))).toString(16);
  return clamped.length === 1 ? `0${clamped}` : clamped;
}

function darken(hex: string, amount: number): string {
  const rgb = parseHex(hex);
  if (rgb === null) return hex;
  const factor = 1 - amount;
  return `#${toHex(rgb.r * factor)}${toHex(rgb.g * factor)}${toHex(rgb.b * factor)}`;
}

function withAlpha(hex: string, alpha: number): string {
  const rgb = parseHex(hex);
  if (rgb === null) return hex;
  return `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${alpha})`;
}

export class Appearance {
  private mode: ThemeMode = DEFAULT_THEME_MODE;
  private accent = DEFAULT_ACCENT_COLOR;
  private font = '';
  private systemPrefersDark = true;
  private readonly listeners: Array<() => void> = [];
  private persist: ((mode: ThemeMode, accent: string) => void) | null = null;
  private uiFont: UiFont | null = null;
  private fontFaces: readonly SystemFontFace[] | null = null;
  private loadFontFaces: (() => Promise<readonly SystemFontFace[]>) | null = null;
  private fontFileUrl: (faceId: string) => string = faceId => faceId;

  constructor() {
    this.restoreFromCache();
    this.watchSystemPreference();
    this.applyToDom();
  }

  useUiFont(
    uiFont: UiFont,
    loadFaces: () => Promise<readonly SystemFontFace[]>,
    fileUrl: (faceId: string) => string,
  ): void {
    this.uiFont = uiFont;
    this.loadFontFaces = loadFaces;
    this.fontFileUrl = fileUrl;
    this.applyFont();
  }

  fontFamily(): string {
    return this.font;
  }

  setPersistence(persist: (mode: ThemeMode, accent: string) => void): void {
    this.persist = persist;
  }

  themeMode(): ThemeMode {
    return this.mode;
  }

  accentColor(): string {
    return this.accent;
  }

  resolvedTheme(): ResolvedTheme {
    if (this.mode === 'system') return this.systemPrefersDark ? 'dark' : 'light';
    return this.mode;
  }

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  setThemeMode(mode: ThemeMode): void {
    this.mode = mode;
    this.cache();
    this.applyToDom();
    this.persist?.(this.mode, this.accent);
  }

  setAccentColor(color: string): void {
    this.accent = color;
    this.cache();
    this.applyToDom();
    this.persist?.(this.mode, this.accent);
  }

  applyFromHost(mode: ThemeMode | undefined, accent: string | undefined, fontFamily?: string): void {
    this.mode = mode ?? DEFAULT_THEME_MODE;
    this.accent = accent ?? DEFAULT_ACCENT_COLOR;
    if (fontFamily !== undefined) this.font = fontFamily;
    this.cache();
    this.applyToDom();
  }

  private applyFont(): void {
    const uiFont = this.uiFont;
    if (uiFont === null) return;

    const load = this.loadFontFaces;
    if (this.font && this.fontFaces === null && load !== null) {
      this.loadFontFaces = null;
      load().then(
        faces => {
          this.fontFaces = faces;
          this.applyFont();
        },
        () => {
          this.loadFontFaces = load;
        });
    }
    uiFont.apply(this.font, this.fontFaces ?? [], this.fontFileUrl);
  }

  private cache(): void {
    try {
      window.localStorage.setItem(STORAGE_KEY_MODE, this.mode);
      window.localStorage.setItem(STORAGE_KEY_ACCENT, this.accent);
      window.localStorage.setItem(STORAGE_KEY_FONT, this.font);
    } catch {
      // Storage refused; the theme still applies, it is just re-fetched on the next load.
    }
  }

  private restoreFromCache(): void {
    try {
      const cachedMode = window.localStorage.getItem(STORAGE_KEY_MODE);
      if (cachedMode === 'light' || cachedMode === 'dark' || cachedMode === 'system') {
        this.mode = cachedMode;
      }
      const cachedAccent = window.localStorage.getItem(STORAGE_KEY_ACCENT);
      if (cachedAccent) this.accent = cachedAccent;
      this.font = window.localStorage.getItem(STORAGE_KEY_FONT) ?? '';
    } catch {
      // No cache; the first paint uses the defaults and the host corrects it.
    }
  }

  private watchSystemPreference(): void {
    if (typeof window.matchMedia !== 'function') return;
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    this.systemPrefersDark = query.matches;

    const onChange = () => {
      this.systemPrefersDark = query.matches;
      if (this.mode === 'system') this.applyToDom();
    };

    // `addEventListener` on a MediaQueryList is Safari 14; the deprecated `addListener` is the one
    // the compatibility floor has.
    if (typeof query.addEventListener === 'function') query.addEventListener('change', onChange);
    else if (typeof query.addListener === 'function') query.addListener(onChange);
  }

  private applyToDom(): void {
    const root = document.documentElement;
    const theme = this.resolvedTheme();
    root.classList.remove('light');
    root.classList.remove('dark');
    root.classList.add(theme);
    const colors = {
      accent: this.accent,
      hover: darken(this.accent, 0.2),
      muted: withAlpha(this.accent, 0.15),
    };
    root.style.setProperty('--color-accent', colors.accent);
    root.style.setProperty('--color-accent-hover', colors.hover);
    root.style.setProperty('--color-accent-muted', colors.muted);
    // The three properties above are inert on the compatibility floor, where the same colours have
    // to arrive as rules instead.
    if (!supportsCustomProperties()) applyAccentStyles(document, colors);
    this.applyFont();

    for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
  }
}

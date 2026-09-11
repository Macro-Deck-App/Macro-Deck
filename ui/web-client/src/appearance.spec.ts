import {
  setCustomPropertySupportForTesting,
  type SystemFontFace,
  UiFont,
  type UiFontBackend,
} from '@macro-deck/runtime';
import { Appearance, DEFAULT_ACCENT_COLOR } from './appearance';
import { RenderingModeStore, SIMPLE_RENDERING_CLASS } from './rendering-mode';

describe('appearance', () => {
  let matchMediaStub: (query: string) => MediaQueryList;
  let prefersDark: boolean;

  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.className = '';
    prefersDark = true;
    matchMediaStub = () => ({
      matches: prefersDark,
      addEventListener: () => undefined,
      addListener: () => undefined,
    }) as unknown as MediaQueryList;
    (window as unknown as { matchMedia: unknown }).matchMedia = matchMediaStub;
  });

  const theme = () => (document.documentElement.classList.contains('dark') ? 'dark' : 'light');

  it('resolves system against the device preference and a named mode against itself', () => {
    prefersDark = false;
    const appearance = new Appearance();
    expect(appearance.resolvedTheme()).toBe('light');

    appearance.setThemeMode('dark');
    expect(appearance.resolvedTheme()).toBe('dark');
    expect(theme()).toBe('dark');

    appearance.setThemeMode('light');
    expect(theme()).toBe('light');
  });

  it('paints the first frame in the theme this device was last in', () => {
    prefersDark = true;
    new Appearance().setThemeMode('light');

    document.documentElement.className = '';
    // A fresh load, before the host has answered: flashing dark and correcting to light is exactly
    // what the cache exists to prevent.
    new Appearance();

    expect(theme()).toBe('light');
  });

  it('lets the host overrule what this device remembered', () => {
    const appearance = new Appearance();
    appearance.setThemeMode('light');

    appearance.applyFromHost('dark', '#ff0000');

    expect(appearance.themeMode()).toBe('dark');
    expect(appearance.accentColor()).toBe('#ff0000');
    expect(theme()).toBe('dark');
  });

  it('derives the accent hover and muted variants from the accent itself', () => {
    const appearance = new Appearance();
    appearance.setAccentColor('#804020');

    const root = document.documentElement;
    expect(root.style.getPropertyValue('--color-accent')).toBe('#804020');
    // Hover is the accent darkened, muted is the accent at low alpha - both have to move with it, or
    // a custom accent leaves the default blue showing through on every hover.
    expect(root.style.getPropertyValue('--color-accent-hover')).toBe('#66331a');
    expect(root.style.getPropertyValue('--color-accent-muted')).toBe('rgba(128, 64, 32, 0.15)');
  });

  it('keeps an unparseable accent rather than painting the deck a wrong colour', () => {
    const appearance = new Appearance();
    appearance.setAccentColor('not-a-colour');

    expect(document.documentElement.style.getPropertyValue('--color-accent-hover')).toBe('not-a-colour');
  });

  it('starts from the default accent when nothing is cached', () => {
    expect(new Appearance().accentColor()).toBe(DEFAULT_ACCENT_COLOR);
  });

  describe('global font', () => {
    const inter: SystemFontFace = {
      faceId: 'inter-400', family: 'Inter', weight: 400, width: 5, slant: 'upright', styleName: 'Regular',
      remoteRenderable: true,
    };
    const added: unknown[] = [];
    const backend: UiFontBackend = {
      create: (family, source) => ({ family, source }),
      add: face => added.push(face),
      remove: face => added.splice(added.indexOf(face), 1),
      onLoadingDone: () => undefined,
    };

    beforeEach(() => {
      added.length = 0;
      setCustomPropertySupportForTesting(true);
    });

    afterEach(() => {
      setCustomPropertySupportForTesting(null);
      document.documentElement.style.removeProperty('--font-sans');
    });

    const withFont = () => {
      const appearance = new Appearance();
      appearance.useUiFont(new UiFont(document.documentElement, backend), () => Promise.resolve([inter]),
        faceId => `http://host/api/system/fonts/${faceId}/file`);
      return appearance;
    };
    const fontSans = () => document.documentElement.style.getPropertyValue('--font-sans');

    it('uses the host font and downloads its faces from the host', async () => {
      const appearance = withFont();

      appearance.applyFromHost('dark', '#ff0000', 'Inter');
      await Promise.resolve();

      expect(fontSans()).toContain('"Inter"');
      expect(added).toEqual([{ family: 'MacroDeckUiFont', source: 'url(http://host/api/system/fonts/inter-400/file)' }]);
    });

    it('returns to the system font when the host clears it', () => {
      const appearance = withFont();
      appearance.applyFromHost('dark', '#ff0000', 'Inter');

      appearance.applyFromHost('dark', '#ff0000', '');

      expect(fontSans()).toBe('');
    });

    it('keeps the font when a host that does not know the setting answers', () => {
      const appearance = withFont();
      appearance.applyFromHost('dark', '#ff0000', 'Inter');

      appearance.applyFromHost('light', '#ff0000', undefined);

      expect(appearance.fontFamily()).toBe('Inter');
      expect(fontSans()).toContain('"Inter"');
    });

    it('paints the first frame in the font this device last showed', () => {
      withFont().applyFromHost('dark', '#ff0000', 'Inter');
      document.documentElement.style.removeProperty('--font-sans');

      withFont();

      expect(fontSans()).toContain('"Inter"');
    });
  });
});

describe('rendering mode', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.className = '';
  });

  it('marks the document so one rule can simplify every widget at once', () => {
    const store = new RenderingModeStore();
    expect(document.documentElement.classList.contains(SIMPLE_RENDERING_CLASS)).toBeFalse();

    store.set('simple');
    expect(store.simple()).toBeTrue();
    expect(document.documentElement.classList.contains(SIMPLE_RENDERING_CLASS)).toBeTrue();

    store.set('standard');
    expect(document.documentElement.classList.contains(SIMPLE_RENDERING_CLASS)).toBeFalse();
  });

  it('remembers the choice on this device, because it is the device that is slow', () => {
    new RenderingModeStore().set('simple');

    document.documentElement.className = '';
    expect(new RenderingModeStore().simple()).toBeTrue();
  });

  it('tells its listeners, so a deck already on screen is repainted', () => {
    const store = new RenderingModeStore();
    let changes = 0;
    store.onChange(() => { changes++; });

    store.set('simple');
    expect(changes).toBe(1);

    // Setting the mode it is already in is not a change and must not repaint the deck.
    store.set('simple');
    expect(changes).toBe(1);
  });
});

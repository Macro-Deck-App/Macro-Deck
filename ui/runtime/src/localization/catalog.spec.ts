import { AppStrings } from './generated/app-strings';
import { ClientAppStrings, ClientAppStringsDefaults } from './generated/client-app-strings';
import { DEFAULT_CULTURE, LocalizationCatalog } from './catalog';

describe('LocalizationCatalog', () => {
  const scopeAndKey = (qualified: string) => {
    const at = qualified.indexOf(':');
    return { scope: qualified.slice(0, at), key: qualified.slice(at + 1) };
  };

  it('paints every key a client can render before a host has answered', () => {
    const catalog = new LocalizationCatalog();

    expect(catalog.culture()).toBe(DEFAULT_CULTURE);

    const unresolved = Object.keys(ClientAppStringsDefaults).filter(qualified => {
      const { scope, key } = scopeAndKey(qualified);
      return catalog.translate(scope, key).indexOf('[[') >= 0;
    });

    expect(unresolved).toEqual([]);
  });

  it('takes the host wording over the bundled one', () => {
    const catalog = new LocalizationCatalog();
    const { scope, key } = scopeAndKey(ClientAppStrings.Auth.SignIn);

    catalog.apply({ culture: 'de-DE', translations: { [ClientAppStrings.Auth.SignIn]: 'Anmelden' } });

    expect(catalog.culture()).toBe('de-DE');
    expect(catalog.translate(scope, key)).toBe('Anmelden');
  });

  it('keeps every key the host catalogue did not carry', () => {
    const catalog = new LocalizationCatalog();
    const { scope, key } = scopeAndKey(ClientAppStrings.Auth.Username);

    catalog.apply({ culture: 'de-DE', translations: { [ClientAppStrings.Auth.SignIn]: 'Anmelden' } });

    // A plugin scope the host has not loaded yet, or a key added after this build.
    expect(catalog.translate(scope, key)).toBe(ClientAppStringsDefaults[ClientAppStrings.Auth.Username]);
  });

  it('starts from the bundled wording again rather than accumulating catalogues', () => {
    const catalog = new LocalizationCatalog();
    const { scope, key } = scopeAndKey(ClientAppStrings.Auth.SignIn);

    catalog.apply({ culture: 'de-DE', translations: { [ClientAppStrings.Auth.SignIn]: 'Anmelden' } });
    catalog.apply({ culture: 'en', translations: {} });

    expect(catalog.translate(scope, key)).toBe(ClientAppStringsDefaults[ClientAppStrings.Auth.SignIn]);
  });

  it('paints what only the host carries, which is most of what a deck shows', () => {
    const catalog = new LocalizationCatalog();
    const { scope, key } = scopeAndKey(AppStrings.Widgets.MusicPlayer.NothingPlaying);

    expect(catalog.translate(scope, key)).toContain('[[');

    catalog.apply({ translations: { [AppStrings.Widgets.MusicPlayer.NothingPlaying]: 'Nichts wird abgespielt' } });

    expect(catalog.translate(scope, key)).toBe('Nichts wird abgespielt');
  });

  it('falls back to the browser-neutral default when the host names no culture', () => {
    const catalog = new LocalizationCatalog();

    catalog.apply({ culture: '', translations: {} });

    expect(catalog.culture()).toBe(DEFAULT_CULTURE);
  });

  it('renders an unresolved key conspicuously rather than blank', () => {
    const catalog = new LocalizationCatalog();

    expect(catalog.translate('plugin.nope', 'Missing')).toBe('[[plugin.nope:Missing]]');
  });

  it('substitutes placeholders', () => {
    const catalog = new LocalizationCatalog();
    catalog.apply({ translations: { 'x:Greeting': 'Hello {name}' } });

    expect(catalog.translate('x', 'Greeting', { name: 'Deck' })).toBe('Hello Deck');
  });

  it('picks a plural form by count, and falls back to the one every family carries', () => {
    const catalog = new LocalizationCatalog();
    catalog.apply({ translations: { 'x:Items.One': 'one item', 'x:Items.Other': '{count} items' } });

    expect(catalog.translate('x', 'Items', { count: 1 })).toBe('one item');
    expect(catalog.translate('x', 'Items', { count: 5 })).toBe('5 items');
  });

  it('resolves an argument that is itself localized text', () => {
    const catalog = new LocalizationCatalog();
    catalog.apply({
      translations: { 'x:Required': '{field} is required', 'x:Name': 'Name' },
    });

    const message = catalog.translate('x', 'Required', {
      field: { $localized: { scope: 'x', key: 'Name' } },
    });

    // Without the argument pass this renders "[object Object] is required".
    expect(message).toBe('Name is required');
  });

  it('tells a subscriber the language changed, because every widget holds text', () => {
    const catalog = new LocalizationCatalog();
    let changes = 0;
    const stop = catalog.onChange(() => changes++);

    catalog.apply({ culture: 'de-DE', translations: {} });
    stop();
    catalog.apply({ culture: 'fr-FR', translations: {} });

    expect(changes).toBe(1);
  });
});

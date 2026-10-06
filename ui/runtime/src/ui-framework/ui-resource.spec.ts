import { hasIconAppearances, iconAppearanceContext, isIconUiResource, uiResourceUrl } from './ui-resource';

describe('ui resource urls', () => {
  const base = 'http://host';

  it('adds the size next to the version', () => {
    expect(uiResourceUrl(base, { resourceId: 'r', contentHash: 'h' }, 128))
      .toBe('http://host/api/ui/resources/r?v=h&size=128');
  });

  it('adds the size to a resource without a version', () => {
    expect(uiResourceUrl(base, { resourceId: 'r' }, 512)).toBe('http://host/api/ui/resources/r?size=512');
  });

  it('leaves the url unchanged without a size', () => {
    expect(uiResourceUrl(base, { resourceId: 'r', contentHash: 'h' })).toBe('http://host/api/ui/resources/r?v=h');
    expect(uiResourceUrl(base, { resourceId: 'r' })).toBe('http://host/api/ui/resources/r');
  });

  it('knows which resources are icons', () => {
    expect(isIconUiResource({ resourceId: 'app.macro-deck.widget-icon.icon-pack.1' })).toBeTrue();
    expect(isIconUiResource({ resourceId: 'app.macro-deck.plugin-icon.1' })).toBeTrue();
    expect(isIconUiResource({ resourceId: 'app.macro-deck.widget-icon-provider.1' })).toBeFalse();
    expect(isIconUiResource({ resourceId: 'app.macro-deck.weather.clear-day' })).toBeFalse();
    expect(isIconUiResource(undefined)).toBeFalse();
  });

  describe('icon appearance context', () => {
    const dark = iconAppearanceContext('dark', false);
    const reduced = iconAppearanceContext('light', true);

    it('asks an icon with appearances for the one that fits the theme and motion setting', () => {
      expect(uiResourceUrl(base, { resourceId: 'app.macro-deck.widget-icon.icon-pack.1.a', contentHash: 'h' }, 128, dark))
        .toBe('http://host/api/ui/resources/app.macro-deck.widget-icon.icon-pack.1.a?v=h&size=128&colorScheme=dark&motion=animated');
      expect(uiResourceUrl(base, { resourceId: 'app.macro-deck.plugin-icon.1.a' }, undefined, reduced))
        .toBe('http://host/api/ui/resources/app.macro-deck.plugin-icon.1.a?colorScheme=light&motion=static');
    });

    it('keeps the url of an icon without appearances and of every other resource exactly as before', () => {
      expect(uiResourceUrl(base, { resourceId: 'app.macro-deck.widget-icon.icon-pack.1', contentHash: 'h' }, 128, dark))
        .toBe('http://host/api/ui/resources/app.macro-deck.widget-icon.icon-pack.1?v=h&size=128');
      expect(uiResourceUrl(base, { resourceId: 'app.macro-deck.weather.a' }, undefined, dark))
        .toBe('http://host/api/ui/resources/app.macro-deck.weather.a');
    });

    it('recognises the appearance marker only on icon resources', () => {
      expect(hasIconAppearances({ resourceId: 'app.macro-deck.widget-icon.icon-pack.1.a' })).toBeTrue();
      expect(hasIconAppearances({ resourceId: 'app.macro-deck.widget-icon.icon-pack.1' })).toBeFalse();
      expect(hasIconAppearances({ resourceId: 'app.macro-deck.weather.a' })).toBeFalse();
      expect(hasIconAppearances(undefined)).toBeFalse();
    });
  });
});

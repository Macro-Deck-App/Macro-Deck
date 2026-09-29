import { isIconUiResource, uiResourceUrl } from './ui-resource';

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
});

import { isStorePackageId } from './store-package-id';

describe('isStorePackageId', () => {
  it('accepts the ids the manifest reference calls valid', () => {
    for (const id of ['com.example.hue-lights', 'com.example', 'my-plugin2.a']) {
      expect(isStorePackageId(id)).withContext(id).toBeTrue();
    }
  });

  it('rejects everything the manifest reference calls invalid, and anything that is not a plain id', () => {
    for (const id of ['com.Example.hue_lights', 'myplugin', '-my.a', 'my--plugin.a', 'my-.a', '1a.b', 'a..b', 'a.b.',
      '../../settings', 'a.b/c', 'a.b?x=1', 'a.b\n', '', null, undefined, 42]) {
      expect(isStorePackageId(id)).withContext(String(id)).toBeFalse();
    }
  });

  it('allows at most 128 characters', () => {
    expect(isStorePackageId(`a.${'b'.repeat(126)}`)).toBeTrue();
    expect(isStorePackageId(`a.${'b'.repeat(127)}`)).toBeFalse();
  });
});

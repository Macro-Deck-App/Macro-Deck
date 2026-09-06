import { FontFaceBackend, FontLoader, internalFontFamily } from './fonts';

describe('font loader', () => {
  interface Pending {
    family: string;
    source: string;
    descriptors: FontFaceDescriptors;
    resolve(): void;
    reject(): void;
  }

  let pending: Pending[];
  let added: number;
  let backend: FontFaceBackend;

  beforeEach(() => {
    pending = [];
    added = 0;
    backend = {
      create: (family, source, descriptors) => ({
        load: () => new Promise<unknown>((resolve, reject) => {
          pending.push({ family, source, descriptors, resolve: () => resolve({}), reject: () => reject(new Error('nope')) });
        }),
      }),
      add: () => { added++; },
    };
  });

  // A microtask drain rather than a timer: every promise here resolves immediately, and a spec that
  // waits on a real timeout stops working the moment another suite installs a fake clock.
  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) await Promise.resolve();
  };

  it('needs nothing for text that names no face', () => {
    const loader = new FontLoader('http://host', backend);

    expect(loader.ready('')).toBeTrue();
    expect(pending.length).toBe(0);
  });

  it('holds text back until the face has actually been registered', async () => {
    const loader = new FontLoader('http://host', backend);

    expect(loader.ready('inter-400-5-upright')).toBeFalse();
    expect(pending.length).toBe(1);

    pending[0].resolve();
    await settle();

    // Ready only once it was added to the document, never merely once the fetch resolved.
    expect(added).toBe(1);
    expect(loader.ready('inter-400-5-upright')).toBeTrue();
  });

  it('releases text in the fallback face when the face cannot be loaded', async () => {
    const loader = new FontLoader('http://host', backend);
    loader.ready('broken-400-5-upright');

    pending[0].reject();
    await settle();

    expect(loader.ready('broken-400-5-upright')).toBeTrue();
    expect(added).toBe(0);
  });

  it('fetches one face once however many callers ask for it', () => {
    const loader = new FontLoader('http://host', backend);

    loader.ready('inter-400-5-upright');
    loader.ready('inter-400-5-upright');
    loader.ready('inter-400-5-upright');

    expect(pending.length).toBe(1);
  });

  it('registers under a synthetic family so an installed font of the same name cannot win', () => {
    const loader = new FontLoader('http://host', backend);
    loader.ready('inter-400-5-upright');

    expect(pending[0].family).toBe(internalFontFamily('inter-400-5-upright'));
    expect(pending[0].family).not.toContain('inter ');
    expect(pending[0].source).toBe('url(http://host/api/system/fonts/inter-400-5-upright/file)');
  });

  it('carries the weight and slant the face id resolves to', () => {
    const loader = new FontLoader('http://host', backend);

    loader.ready('inter-700-5-italic');
    expect(pending[0].descriptors).toEqual({ weight: '700', style: 'italic' });

    loader.ready('inter-400-5-upright');
    expect(pending[1].descriptors).toEqual({ weight: '400', style: 'normal' });

    // The catalog appends a collision suffix to faces sharing all four id components; a face that
    // carries one must still get its descriptors.
    loader.ready('inter-600-5-upright-2');
    expect(pending[2].descriptors).toEqual({ weight: '600', style: 'normal' });
  });

  it('tells its listeners when a face settles, so held-back text can be painted', async () => {
    const loader = new FontLoader('http://host', backend);
    let changes = 0;
    loader.onChange(() => { changes++; });

    loader.ready('inter-400-5-upright');
    expect(changes).toBe(0);

    pending[0].resolve();
    await settle();

    expect(changes).toBe(1);
  });

  it('never holds text back on an engine with no FontFace API at all', () => {
    const loader = new FontLoader('http://host', null);

    loader.ready('inter-400-5-upright');
    expect(loader.ready('inter-400-5-upright')).toBeTrue();
  });
});

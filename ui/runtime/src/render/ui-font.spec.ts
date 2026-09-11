import type { SystemFontFace } from '../protocol/messages/system';
import { setCustomPropertySupportForTesting } from './custom-properties';
import { UI_FONT_FAMILY, UiFont, UiFontBackend } from './ui-font';

interface FakeFace {
  family: string;
  source: string;
  descriptors: FontFaceDescriptors;
}

function fakeBackend() {
  const added: FakeFace[] = [];
  let loadingDone: (faces: readonly unknown[]) => void = () => undefined;
  const backend: UiFontBackend = {
    create: (family, source, descriptors) => ({ family, source, descriptors }),
    add: face => added.push(face as FakeFace),
    remove: face => added.splice(added.indexOf(face as FakeFace), 1),
    onLoadingDone: listener => (loadingDone = listener),
  };
  return { backend, added, finishLoading: (faces: readonly unknown[]) => loadingDone(faces) };
}

const face = (faceId: string, family: string, weight: number, slant: SystemFontFace['slant'] = 'upright',
  remoteRenderable = true): SystemFontFace =>
  ({ faceId, family, weight, width: 5, slant, styleName: '', remoteRenderable });

const catalog: SystemFontFace[] = [
  face('inter-400', 'Inter', 400),
  face('inter-700-italic', 'Inter', 700, 'italic'),
  face('inter-900', 'Inter', 900, 'upright', false),
  face('acme-400', 'Acme', 400),
];

const fileUrl = (faceId: string) => `http://host/api/system/fonts/${faceId}/file`;

describe('UiFont', () => {
  let root: HTMLElement;

  beforeEach(() => {
    root = document.createElement('div');
    setCustomPropertySupportForTesting(true);
  });

  afterEach(() => setCustomPropertySupportForTesting(null));

  it('puts the chosen family ahead of its downloadable faces and the system stack', () => {
    new UiFont(root, fakeBackend().backend).apply('Inter', catalog, fileUrl);

    expect(root.style.getPropertyValue('--font-sans'))
      .toBe(`"Inter", ${UI_FONT_FAMILY}, var(--font-sans-system)`);
  });

  it('offers every downloadable face of the family from the host, with its own weight and style', () => {
    const { backend, added } = fakeBackend();

    new UiFont(root, backend).apply('Inter', catalog, fileUrl);

    expect(added).toEqual([
      { family: UI_FONT_FAMILY, source: `url(${fileUrl('inter-400')})`, descriptors: { weight: '400', style: 'normal' } },
      {
        family: UI_FONT_FAMILY,
        source: `url(${fileUrl('inter-700-italic')})`,
        descriptors: { weight: '700', style: 'italic' },
      },
    ]);
  });

  it('replaces the previous family instead of stacking faces', () => {
    const { backend, added } = fakeBackend();
    const font = new UiFont(root, backend);

    font.apply('Inter', catalog, fileUrl);
    font.apply('Acme', catalog, fileUrl);

    expect(added.map(f => f.source)).toEqual([`url(${fileUrl('acme-400')})`]);
    expect(root.style.getPropertyValue('--font-sans')).toContain('"Acme"');
  });

  it('returns to the system font when the family is cleared', () => {
    const { backend, added } = fakeBackend();
    const font = new UiFont(root, backend);
    font.apply('Inter', catalog, fileUrl);

    font.apply('', catalog, fileUrl);

    expect(root.style.getPropertyValue('--font-sans')).toBe('');
    expect(added).toEqual([]);
  });

  it('reports a change when the font changes and when one of its own faces finishes loading', () => {
    const { backend, added, finishLoading } = fakeBackend();
    const font = new UiFont(root, backend);
    let changes = 0;
    font.onChange(() => changes++);

    font.apply('Inter', catalog, fileUrl);
    font.apply('Inter', catalog, fileUrl);
    finishLoading([{ some: 'widget face' }]);
    finishLoading([added[0]]);

    expect(changes).toBe(2);
    expect(font.version()).toBe(2);
  });

  it('sets the family inline where custom properties are not supported', () => {
    setCustomPropertySupportForTesting(false);

    new UiFont(root, fakeBackend().backend).apply('Inter', catalog, fileUrl);

    expect(root.style.fontFamily).toContain('Inter');
    expect(root.style.getPropertyValue('--font-sans')).toBe('');
  });
});

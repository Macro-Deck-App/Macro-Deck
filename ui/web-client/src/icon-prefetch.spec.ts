import {
  resolveFolderGrid,
  WidgetType,
  type ActionButtonData,
  type Folder,
  type GridWidget,
  type ProfileGridDefaults,
} from '@macro-deck/runtime';
import { IconPrefetch } from './icon-prefetch';

function button(id: string, folderId: string, iconId = 'i'): GridWidget {
  const data: ActionButtonData = { label: '', iconId };
  return { id, folderId, x: 0, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data };
}

function makeFolder(id: string, widgets: GridWidget[], overrides: Partial<Folder> = {}): Folder {
  return {
    id, name: id, parentId: null, order: 0, isExpanded: false, isDefault: false,
    cols: null, rows: null, background: '', spacing: null, borderRadius: null,
    viewId: 'macrodeck.widget-grid', viewConfiguration: null, widgets,
    ...overrides,
  } as Folder;
}

describe('IconPrefetch', () => {
  let realInnerWidth: number;
  let realInnerHeight: number;
  let realDevicePixelRatio: number;

  beforeEach(() => {
    realInnerWidth = window.innerWidth;
    realInnerHeight = window.innerHeight;
    realDevicePixelRatio = window.devicePixelRatio;
    Object.defineProperty(window, 'innerWidth', { value: 800, configurable: true });
    Object.defineProperty(window, 'innerHeight', { value: 480, configurable: true });
    Object.defineProperty(window, 'devicePixelRatio', { value: 1, configurable: true });
  });

  afterEach(() => {
    Object.defineProperty(window, 'innerWidth', { value: realInnerWidth, configurable: true });
    Object.defineProperty(window, 'innerHeight', { value: realInnerHeight, configurable: true });
    Object.defineProperty(window, 'devicePixelRatio', { value: realDevicePixelRatio, configurable: true });
  });

  const sizesOf = (urls: readonly string[]): number[] =>
    urls.map(url => Number(new URL(url).searchParams.get('size')));

  const gridResolver = (folders: readonly Folder[], profile: ProfileGridDefaults) =>
    (folder: Folder) => {
      const ancestors: Folder[] = [];
      let parentId = folder.parentId;
      while (parentId !== null) {
        const parent = folders.find(candidate => candidate.id === parentId);
        if (!parent) break;
        ancestors.push(parent);
        parentId = parent.parentId;
      }
      return resolveFolderGrid(folder, profile, ancestors);
    };

  const prefetch = (
    folders: readonly Folder[],
    currentFolderId: string | null,
    profile: ProfileGridDefaults = {},
  ): string[] => {
    const urls: string[] = [];
    const icons = new IconPrefetch({
      baseUrl: () => 'http://host',
      folders: () => folders,
      currentFolderId: () => currentFolderId,
      resolveGrid: gridResolver(folders, profile),
      outerMargin: 0,
      schedule: work => work(),
      load: url => { urls.push(url); return Promise.resolve(); },
    });
    icons.warm();
    return urls;
  };

  it('warms the profile\'s shape, not the built-in 5x3, for a folder that states no grid', () => {
    const folder = makeFolder('f', [button('w1', 'f')]);

    const urls = prefetch([folder], null, { columns: 8, rows: 4 });

    expect(urls.length).toBe(1);
    expect(sizesOf(urls)).toEqual([128]);
  });

  it('lets a subfolder inherit its ancestor\'s shape', () => {
    const parent = makeFolder('parent', [], { cols: 10, rows: 5 });
    const child = makeFolder('child', [button('w1', 'child')], { parentId: 'parent' });

    const urls = prefetch([parent, child], 'parent');

    expect(urls.length).toBe(1);
    expect(sizesOf(urls)).toEqual([128]);
  });

  it('matches the deck\'s own outer margin, not the built-in 4px default', () => {
    Object.defineProperty(window, 'innerWidth', { value: 800, configurable: true });
    Object.defineProperty(window, 'innerHeight', { value: 442, configurable: true });
    const folder = makeFolder('f', [button('w1', 'f')], { cols: 5, rows: 3, spacing: 12 });

    const urls = prefetch([folder], null);

    expect(urls.length).toBe(1);
    expect(sizesOf(urls)).toEqual([256]);
  });

  it('still excludes the current folder', () => {
    const current = makeFolder('current', [button('w1', 'current', 'visible-anyway')]);
    const other = makeFolder('other', [button('w2', 'other', 'wanted')], { cols: 5, rows: 3, spacing: 12 });

    const urls = prefetch([current, other], 'current');

    expect(urls.length).toBe(1);
    expect(new URL(urls[0]).pathname).toContain('wanted');
  });

  it('warms the same icon at the same bucket across folders only once', () => {
    const a = makeFolder('a', [button('w1', 'a', 'shared')], { cols: 5, rows: 3, spacing: 12 });
    const b = makeFolder('b', [button('w2', 'b', 'shared')], { cols: 5, rows: 3, spacing: 12 });

    const urls = prefetch([a, b], null);

    expect(urls.length).toBe(1);
  });

  it('warms nothing while the viewport reports zero, but keeps retrying', () => {
    Object.defineProperty(window, 'innerWidth', { value: 0, configurable: true });
    Object.defineProperty(window, 'innerHeight', { value: 0, configurable: true });
    jasmine.clock().install();
    try {
      const folder = makeFolder('f', [button('w1', 'f')]);
      const urls: string[] = [];
      const icons = new IconPrefetch({
        baseUrl: () => 'http://host',
        folders: () => [folder],
        currentFolderId: () => null,
        resolveGrid: gridResolver([folder], {}),
        outerMargin: 0,
        schedule: work => work(),
        load: url => { urls.push(url); return Promise.resolve(); },
      });

      icons.warm();
      expect(urls.length).toBe(0);

      Object.defineProperty(window, 'innerWidth', { value: 800, configurable: true });
      Object.defineProperty(window, 'innerHeight', { value: 480, configurable: true });
      jasmine.clock().tick(1000);

      expect(urls.length).toBe(1);
    } finally {
      jasmine.clock().uninstall();
    }
  });
});

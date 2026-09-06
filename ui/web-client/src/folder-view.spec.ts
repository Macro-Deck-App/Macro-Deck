import { LocalizationCatalog, UiSessionStore, type UiConnection, type UiRenderHost } from '@macro-deck/runtime';
import { FolderView } from './folder-view';

describe('folder view', () => {
  let requests: Array<{ type: string; payload: unknown; resolve(value: unknown): void; reject(): void }>;
  let connection: UiConnection;
  let sessions: UiSessionStore;
  let backs: number;
  let canGoBack: boolean;
  let host: UiRenderHost;
  let view: FolderView | null;

  const catalog = new LocalizationCatalog();
  const english = (qualified: string) => {
    const at = qualified.indexOf(':');
    return catalog.translate(qualified.slice(0, at), qualified.slice(at + 1));
  };

  beforeEach(() => {
    requests = [];
    backs = 0;
    canGoBack = true;
    sessions = new UiSessionStore();
    connection = {
      request: (type: string, payload: unknown) => new Promise((resolve, reject) => {
        requests.push({ type, payload, resolve, reject: () => reject(new Error('refused')) });
      }),
    } as unknown as UiConnection;
    host = {
      localization: catalog,
      resourceUrl: () => null,
      now: () => 0,
      culture: () => 'en',
      simpleRendering: () => false,
      fontFamily: () => null,
      fontReady: () => true,
      emit: () => undefined,
    };
    view = null;
  });

  afterEach(() => {
    if (view !== null) view.destroy();
  });

  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) await Promise.resolve();
  };

  const mount = (): FolderView => {
    view = new FolderView({
      connection,
      sessions,
      localization: catalog,
      host,
      canGoBack: () => canGoBack,
      onBack: () => { backs++; },
    });
    document.body.appendChild(view.element);
    return view;
  };

  const openRequest = () => requests.filter(entry => entry.type === 'OpenFolderUiSession')[0];

  it('tells the built-in grid apart from a view a provider owns', () => {
    expect(FolderView.isProviderView('macrodeck.widget-grid')).toBeFalse();
    expect(FolderView.isProviderView('')).toBeFalse();
    expect(FolderView.isProviderView(null)).toBeFalse();
    expect(FolderView.isProviderView('acme.now-playing')).toBeTrue();
  });

  it('asks the host for the folder rather than drawing a grid at it', () => {
    mount().open('f1');

    expect(openRequest().type).toBe('OpenFolderUiSession');
    expect(openRequest().payload).toEqual([{ folderId: 'f1' }]);
  });

  it('says it is loading, not that the folder is broken, while it waits', () => {
    mount().open('f1');

    // Both states draw nothing; telling someone their folder is broken while it is merely loading
    // would be worse than a moment of blank.
    expect(view!.element.textContent).toContain(english('macrodeck.app:Deck.FolderView.Loading'));
  });

  it('explains an unavailable view, naming the one the folder still asks for', async () => {
    mount().open('f1');
    openRequest().resolve({ accepted: false, viewId: 'acme.now-playing' });
    await settle();

    expect(view!.element.textContent).toContain(english('macrodeck.app:Deck.FolderView.UnavailableHeading'));
    expect(view!.element.textContent).toContain('acme.now-playing');
  });

  it('keeps a way out of a view that asked for none', async () => {
    mount().open('f1');
    openRequest().resolve({ accepted: true, sessionId: 's1', viewId: 'acme.x', navigation: 'hidden' });
    await settle();

    expect(view!.element.querySelector('.wc-folder-view-back')).toBeNull();

    // Unavailable is exactly the case where the button matters most: without it a broken plugin view
    // is a room with no door.
    view!.close();
    view!.open('f2');
    requests.filter(entry => entry.type === 'OpenFolderUiSession')[1].resolve({ accepted: false, viewId: 'acme.x' });
    await settle();

    expect(view!.element.querySelector('.wc-folder-view-back')).not.toBeNull();
  });

  it('draws no back control where there is nowhere to go', async () => {
    canGoBack = false;
    mount().open('f1');
    openRequest().resolve({ accepted: false, viewId: 'acme.x' });
    await settle();

    expect(view!.element.querySelector('.wc-folder-view-back')).toBeNull();
  });

  it('drives Macro Deck navigation from the back control, never the provider', async () => {
    mount().open('f1');
    openRequest().resolve({ accepted: true, sessionId: 's1', viewId: 'acme.x' });
    await settle();

    (view!.element.querySelector('.wc-folder-view-back') as HTMLElement).click();

    expect(backs).toBe(1);
  });

  it('attaches to the session the host accepted, so the tree can be pushed into it', async () => {
    mount().open('f1');
    openRequest().resolve({ accepted: true, sessionId: 's1', viewId: 'acme.x' });
    await settle();

    const attach = requests.filter(entry => entry.type === 'AttachUiSession')[0];
    expect(attach.payload).toEqual([{ sessionId: 's1' }]);
  });

  it('reopens rather than resyncs when asked for a different folder', async () => {
    const mounted = mount();
    mounted.open('f1');
    openRequest().resolve({ accepted: true, sessionId: 's1', viewId: 'acme.x' });
    await settle();

    mounted.open('f2');

    // A dropped session is gone on the host too, so there is nothing to resync to.
    expect(requests.filter(entry => entry.type === 'CloseUiSession').length).toBe(1);
    expect(requests.filter(entry => entry.type === 'OpenFolderUiSession').length).toBe(2);
  });
});

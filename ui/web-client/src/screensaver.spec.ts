import { LocalizationCatalog, UiSessionStore, type UiConnection, type UiNode, type UiRenderHost } from '@macro-deck/runtime';
import { DISMISS_COOLDOWN_MS, ScreenSaver } from './screensaver';

describe('screensaver', () => {
  let requests: Array<{ type: string; payload: unknown; resolve(value: unknown): void; reject(): void }>;
  let connection: UiConnection;
  let sessions: UiSessionStore;
  let dismissed: number;
  let saver: ScreenSaver | null;
  let interactive: boolean;
  let accept: boolean;

  const catalog = new LocalizationCatalog();
  const host: UiRenderHost = {
    localization: catalog,
    resourceUrl: () => null,
    now: () => 0,
    culture: () => 'en',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  };

  const clockTree: UiNode = { id: 'root', type: 'ui.text', properties: { text: '12:00' } } as UiNode;
  const buttonTree: UiNode = {
    id: 'root', type: 'ui.stack', properties: {},
    children: [{ id: 'skip', type: 'ui.button', properties: { events: ['press'] } }],
  } as UiNode;

  beforeEach(() => {
    requests = [];
    dismissed = 0;
    interactive = false;
    accept = true;
    sessions = new UiSessionStore();
    connection = {
      request: (type: string, payload: unknown) => new Promise((resolve, reject) => {
        const entry = { type, payload, resolve, reject: () => reject(new Error('refused')) };
        requests.push(entry);
        if (type === 'OpenScreenSaverUiSession') {
          resolve(accept
            ? { accepted: true, sessionId: `s${requests.length}`, screenSaverId: 'x::clock', interactive }
            : { accepted: false, sessionId: '', code: 'PROVIDER_UNAVAILABLE' });
        } else {
          resolve(undefined);
        }
      }),
    } as unknown as UiConnection;
    saver = null;
  });

  afterEach(() => {
    if (saver !== null) saver.destroy();
  });

  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 6; turn++) await Promise.resolve();
  };

  const mount = (): ScreenSaver => {
    saver = new ScreenSaver({ connection, sessions, localization: catalog, host, onDismiss: () => { dismissed++; } });
    document.body.appendChild(saver.element);
    return saver;
  };

  const sessionId = () => (requests.find(entry => entry.type === 'AttachUiSession')?.payload as [{ sessionId: string }])[0].sessionId;

  const sent = (type: string) => requests.filter(entry => entry.type === type);

  const pointer = (target: Element, type: string) => {
    const event = new Event(type, { bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
  };

  it('opens the surface, attaches and draws the tree while it shows', async () => {
    const view = mount();
    view.show();
    await settle();
    sessions.treeUpdated(sessionId(), 1, clockTree);

    expect(view.element.hidden).toBeFalse();
    expect(view.element.textContent).toContain('12:00');
    expect(view.element.getAttribute('role')).toBe('status');
  });

  it('is dismissed by the first touch, which never reaches what is under it', async () => {
    const view = mount();
    const under = document.createElement('button');
    let pressed = 0;
    under.addEventListener('pointerdown', () => { pressed++; });
    document.body.appendChild(under);
    view.show();
    await settle();
    sessions.treeUpdated(sessionId(), 1, clockTree);

    const event = pointer(view.element, 'pointerdown');

    expect(dismissed).toBe(1);
    expect(event.defaultPrevented).toBeTrue();
    expect(view.isShowing()).toBeFalse();
    expect(view.isDismissing()).toBeTrue();
    expect(pressed).toBe(0);
    expect(sent('CloseUiSession').length).toBe(1);
    under.remove();
  });

  it('keeps covering the deck, see-through, for a moment after the dismissing touch', async () => {
    const view = mount();
    view.show();
    await settle();
    pointer(view.element, 'pointerdown');

    expect(view.element.hidden).toBeFalse();
    expect(view.element.classList).toContain('wc-screensaver-deaf');
    const second = pointer(view.element, 'pointerdown');
    expect(second.defaultPrevented).toBeTrue();
    expect(dismissed).toBe(1);

    await new Promise(resolve => setTimeout(resolve, DISMISS_COOLDOWN_MS + 30));
    expect(view.element.hidden).toBeTrue();
    expect(view.element.classList).not.toContain('wc-screensaver-deaf');
  });

  it('swallows a tap on a press node of a screensaver that declared no interactivity', async () => {
    const view = mount();
    view.show();
    await settle();
    sessions.treeUpdated(sessionId(), 1, buttonTree);
    const button = view.element.querySelector('[data-node-id="skip"]') as Element;
    expect(button).withContext('the button was rendered').not.toBeNull();

    pointer(button, 'pointerdown');
    pointer(button, 'pointerup');

    expect(dismissed).toBe(1);
    expect(view.isShowing()).toBeFalse();
    expect(sent('SendUiEvent').length).toBe(0);
  });

  it('lets a press through to an interactive screensaver and still dismisses anywhere else', async () => {
    interactive = true;
    const view = mount();
    view.show();
    await settle();
    sessions.treeUpdated(sessionId(), 1, buttonTree);
    const button = view.element.querySelector('[data-node-id="skip"]') as Element;

    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerId: 1, button: 0 }));
    button.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, pointerId: 1, button: 0 }));
    await settle();
    expect(dismissed).toBe(0);
    expect(sent('SendUiEvent').some(entry => (entry.payload as [{ name: string }])[0].name === 'press')).toBeTrue();

    pointer(view.element, 'pointerdown');
    expect(dismissed).toBe(1);
  });

  it('is dismissed by a key, which reaches nothing else', async () => {
    const view = mount();
    let reached = 0;
    document.addEventListener('keydown', () => { reached++; });
    view.show();
    await settle();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));

    expect(dismissed).toBe(1);
    expect(reached).toBe(0);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    expect(reached).toBe(0);

    await new Promise(resolve => setTimeout(resolve, DISMISS_COOLDOWN_MS + 30));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    expect(reached).toBe(1);
  });

  it('opens again each time a session that showed something ends', async () => {
    const view = mount();
    view.show();
    await settle();
    const first = sessionId();
    sessions.treeUpdated(first, 1, clockTree);

    sessions.invalidated(first);
    await settle();
    expect(sent('OpenScreenSaverUiSession').length).toBe(2);
    const second = (sent('AttachUiSession')[1].payload as [{ sessionId: string }])[0].sessionId;
    sessions.treeUpdated(second, 1, clockTree);

    sessions.invalidated(second);
    await settle();
    expect(sent('OpenScreenSaverUiSession').length).toBe(3);
    expect(view.isShowing()).toBeTrue();
    expect(dismissed).toBe(0);
  });

  it('hides rather than looping when it loses its session again before anything was shown', async () => {
    const view = mount();
    view.show();
    await settle();
    sessions.treeUpdated(sessionId(), 1, clockTree);

    sessions.clear();
    await settle();
    expect(sent('OpenScreenSaverUiSession').length).toBe(2);

    sessions.treeUpdated('a-deck-widget', 1, clockTree);
    sessions.clear();
    await settle();
    expect(sent('OpenScreenSaverUiSession').length).toBe(2);
    expect(view.isShowing()).toBeFalse();
    expect(dismissed).toBe(1);
  });

  it('gives the deck back when the host has nothing to show', async () => {
    accept = false;
    const view = mount();
    view.show();
    await settle();

    expect(view.element.hidden).toBeTrue();
    expect(dismissed).toBe(1);
  });
});

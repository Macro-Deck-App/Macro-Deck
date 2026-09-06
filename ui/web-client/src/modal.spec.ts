import { ClientAppStrings, LocalizationCatalog, type UiNode, type UiRenderHost } from '@macro-deck/runtime';
import { Client } from './client';
import { ModalHost } from './modal';

const catalog = new LocalizationCatalog();
const english = (qualifiedKey: string): string => {
  const separator = qualifiedKey.indexOf(':');
  return catalog.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1));
};

const host: UiRenderHost = {
  localization: catalog,
  resourceUrl: () => null,
  now: () => 0,
  culture: () => 'en-GB',
  simpleRendering: () => false,
  fontFamily: () => null,
  fontReady: () => true,
  emit: () => undefined,
};

const tree = (): UiNode =>
  ({ id: 'root', type: 'ui.text', properties: { text: 'pick one' } }) as UiNode;

const rows = (): UiNode => ({
  id: 'list',
  type: 'ui.stack',
  properties: {},
  children: [
    { id: 'row-1', type: 'ui.stack', properties: { events: ['press'], answer: 'track-7' }, children: [] },
    { id: 'row-2', type: 'ui.stack', properties: { events: ['press'] }, children: [] },
  ],
}) as UiNode;

const press = (element: HTMLElement) => {
  const pointer = (type: string) => {
    const event = new Event(type, { bubbles: true, cancelable: true }) as Event
      & { clientX: number; clientY: number; pointerId: number; button: number };
    event.clientX = 0;
    event.clientY = 0;
    event.pointerId = 1;
    event.button = 0;
    return event;
  };
  element.dispatchEvent(pointer('pointerdown'));
  element.dispatchEvent(pointer('pointerup'));
};

describe('ModalHost', () => {
  let root: HTMLElement;
  let client: Client;
  let calls: Array<{ type: string; payload: unknown }>;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);

    client = new Client(() => 'http://host', 'client-1');
    calls = [];
    (client.connection as unknown as { request(type: string, payload?: unknown): Promise<unknown> })
      .request = (type: string, payload?: unknown) => {
        calls.push({ type, payload });
        if (type === 'OpenWidgetUiSession' || type === 'OpenModalUiSession') {
          return Promise.resolve({ accepted: true, sessionId: 's1' });
        }
        if (type === 'AttachUiSession') return Promise.resolve({ accepted: true });
        return Promise.resolve(undefined);
      };

    new ModalHost(root, client, host);
  });

  afterEach(() => root.remove());

  const settle = async () => { for (let turn = 0; turn < 8; turn++) await Promise.resolve(); };
  const backdrop = () => root.querySelector('.wc-modal-backdrop') as HTMLElement;
  const open = async (title?: unknown) => {
    client.modal.set({ modalId: 'm1', title: title as never });
    await settle();
  };

  it('stays out of the way until an action opens one', () => {
    expect(backdrop().hasAttribute('hidden')).toBeTrue();
  });

  it('shows the dialog and asks for the tree behind it', async () => {
    await open();

    expect(backdrop().hasAttribute('hidden')).toBeFalse();
    expect(calls.map(call => call.type)).toEqual(['OpenModalUiSession', 'AttachUiSession']);
    // Opening a session subscribes to nothing; without the attach the dialog opens and stays empty.
    expect(calls[1].payload).toEqual([{ sessionId: 's1' }]);
  });

  it('draws the tree the host pushed for it', async () => {
    await open();
    client.sessions.treeUpdated('s1', 1, tree());

    expect(root.querySelector('.wc-modal-content .widget-text')!.textContent).toBe('pick one');
  });

  it('reports a completion with the value the user chose', async () => {
    await open();
    client.sessions.treeUpdated('s1', 1, tree());

    client.completeModal('m1', false, { trackId: 't7' });
    await settle();

    const completion = calls.find(call => call.type === 'CompleteUiModal')!;
    expect(completion.payload).toEqual([{ modalId: 'm1', cancelled: false, value: { trackId: 't7' } }]);
    expect(backdrop().hasAttribute('hidden')).toBeTrue();
  });

  it('answers with what the pressed row carries', async () => {
    await open();
    client.sessions.treeUpdated('s1', 1, rows());

    press(root.querySelector('[data-node-id="row-1"]') as HTMLElement);
    await settle();

    // The client settles it, because the client is the side that knows the press happened, has to take
    // the dialog down, and owns the principal the modal is bound to.
    const completion = calls.find(call => call.type === 'CompleteUiModal')!;
    expect(completion.payload).toEqual([{ modalId: 'm1', cancelled: false, value: 'track-7' }]);
    expect(backdrop().hasAttribute('hidden')).toBeTrue();
  });

  it('routes a press that answers nothing back to the provider instead', async () => {
    await open();
    client.sessions.treeUpdated('s1', 1, rows());

    press(root.querySelector('[data-node-id="row-2"]') as HTMLElement);
    await settle();

    expect(calls.some(call => call.type === 'CompleteUiModal')).toBeFalse();
    expect(calls.some(call => call.type === 'SendUiEvent')).toBeTrue();
  });

  it('settles the modal before it takes the dialog down', async () => {
    await open();
    client.sessions.treeUpdated('s1', 1, rows());

    press(root.querySelector('[data-node-id="row-1"]') as HTMLElement);
    await settle();

    // A modal is bound to the session serving it, and the host cancels it when that session closes.
    // The other order therefore loses every pick to a modal that has already been cancelled.
    const order = calls.map(call => call.type);
    expect(order.indexOf('CompleteUiModal')).toBeLessThan(order.indexOf('CloseUiSession'));
  });

  it('reports a cancellation when the backdrop is clicked', async () => {
    await open();

    backdrop().dispatchEvent(new Event('pointerdown'));
    await settle();

    const completion = calls.find(call => call.type === 'CompleteUiModal')!;
    expect(completion.payload).toEqual([{ modalId: 'm1', cancelled: true, value: undefined }]);
    expect(backdrop().hasAttribute('hidden')).toBeTrue();
  });

  it('reports a cancellation on Escape', async () => {
    await open();

    const escape = new Event('keydown') as Event & { key: string };
    escape.key = 'Escape';
    document.dispatchEvent(escape);
    await settle();

    expect(calls.some(call => call.type === 'CompleteUiModal')).toBeTrue();
  });

  it('closes the session it opened, so the host stops pushing into it', async () => {
    await open();

    client.completeModal('m1', true);
    await settle();

    expect(calls.some(call => call.type === 'CloseUiSession')).toBeTrue();
  });

  it('cancels a modal that is replaced rather than stacking the two', async () => {
    await open();
    client.modal.set({ modalId: 'm2' });
    await settle();

    const cancelled = calls.filter(call => call.type === 'CompleteUiModal');
    expect(cancelled.length).toBe(1);
    expect(cancelled[0].payload).toEqual([{ modalId: 'm1', cancelled: true, value: undefined }]);
  });

  it('resolves a localized title through the client catalogue', async () => {
    await open({ $localized: { scope: 'macrodeck.app', key: 'WebClient.Lock.Title' } });

    const heading = root.querySelector('.wc-modal-title')!.textContent ?? '';
    expect(heading).toBe(english(ClientAppStrings.WebClient.Lock.Title));
  });
});

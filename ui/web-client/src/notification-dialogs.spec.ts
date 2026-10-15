import { ClientAppStrings, LocalizationCatalog, type UiNode, type UiRenderHost } from '@macro-deck/runtime';
import { NotificationDialogs } from './notification-dialogs';
import { NotificationFeed } from './notification-feed';
import { notification, recordingRequest, settle } from './notification-fixtures';

const catalog = new LocalizationCatalog();

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

const english = (qualifiedKey: string): string => {
  const separator = qualifiedKey.indexOf(':');
  return catalog.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1));
};

const form = (): UiNode => ({
  id: 'form',
  type: 'ui.stack',
  properties: {},
  children: [
    { id: 'name', type: 'ui.text-field', properties: { text: '', events: ['change', 'adjust'] }, children: [] },
  ],
}) as UiNode;

describe('notification dialogs', () => {
  let root: HTMLElement;
  let feed: NotificationFeed;
  let wire: ReturnType<typeof recordingRequest>;
  let answer: () => unknown;
  let dialogs: NotificationDialogs;

  const backdrop = (): HTMLElement => root.querySelector('.wc-notification-backdrop') as HTMLElement;
  const visible = (): boolean => !backdrop().hasAttribute('hidden');
  const title = (): string => root.querySelector('.wc-notification-title')!.textContent!;
  const buttons = (): HTMLButtonElement[] =>
    Array.prototype.slice.call(root.querySelectorAll('.wc-notification-actions .wc-btn'));
  const escape = (): void => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
  };

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    answer = () => ({ status: 'accepted' });
    wire = recordingRequest(() => answer());
    feed = new NotificationFeed(wire.request);
    dialogs = new NotificationDialogs(root, feed, host);
  });

  afterEach(() => {
    feed.clear();
    root.remove();
  });

  it('stays hidden until the host pushes a dialog, and ignores toasts', () => {
    expect(visible()).toBeFalse();

    feed.shown(notification({ popup: 'toast' }));

    expect(visible()).toBeFalse();
    expect(dialogs.isOpen()).toBeFalse();
  });

  it('shows one dialog at a time, oldest first, and the next once the host closes it', () => {
    feed.shown(notification({ id: 'a', popup: 'dialog', title: 'First' }));
    feed.shown(notification({ id: 'b', popup: 'dialog', title: 'Second' }));

    expect(title()).toBe('First');

    feed.closed({ id: 'a', revision: 2, reason: 'action', actionId: 'ok' });

    expect(title()).toBe('Second');
    expect(visible()).toBeTrue();

    feed.closed({ id: 'b', revision: 2, reason: 'dismissed' });

    expect(visible()).toBeFalse();
  });

  it('drops a queued dialog the host closed before it came up', () => {
    feed.shown(notification({ id: 'a', popup: 'dialog', title: 'First' }));
    feed.shown(notification({ id: 'b', popup: 'dialog', title: 'Second' }));

    feed.closed({ id: 'b', revision: 2, reason: 'timeout' });
    feed.closed({ id: 'a', revision: 2, reason: 'dismissed' });

    expect(visible()).toBeFalse();
  });

  it('draws title, message, source and action styles', () => {
    feed.shown(notification({
      popup: 'dialog',
      title: 'Delete deck?',
      message: 'This cannot be undone.',
      severity: 'warning',
      actions: [
        { id: 'delete', label: 'Delete', style: 'danger' },
        { id: 'keep', label: 'Keep', style: 'default' },
      ],
    }));

    expect(root.querySelector('.wc-notification-message')!.textContent).toBe('This cannot be undone.');
    expect(root.querySelector('.wc-notification-source-name')!.textContent).toBe('Acme');
    expect(buttons().map(b => b.textContent)).toEqual(['Delete', 'Keep']);
    expect(buttons()[0].classList.contains('wc-btn-danger')).toBeTrue();
    expect(root.querySelector('.wc-notification-dialog')!.getAttribute('role')).toBe('dialog');
  });

  it('answers with the action and stays open until the host closes it', async () => {
    feed.shown(notification({ id: 'a', popup: 'dialog', actions: [{ id: 'go', label: 'Go', style: 'primary' }] }));

    buttons()[0].click();
    await settle();

    expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: 'go' }]);
    expect(visible()).toBeTrue();
    expect(buttons()[0].disabled).toBeFalse();
  });

  it('writes a clock in a dialog with the leading zero the host states', () => {
    const own = document.createElement('div');
    document.body.appendChild(own);
    const ownFeed = new NotificationFeed(wire.request);
    new NotificationDialogs(own, ownFeed, {
      ...host, now: () => Date.parse('2026-01-02T03:04:05Z'), culture: () => 'es', hourCycle: () => 'h23', hourPadded: () => true,
    });
    ownFeed.shown(notification({
      id: 'a', popup: 'dialog',
      content: { id: 'root', type: 'macrodeck.dynamic-text', properties: { format: 'time', value: { $time: { zone: 'UTC' } } } } as UiNode,
    }));

    expect(own.querySelector('.widget-dynamic-text')!.textContent).toBe('03:04');
    ownFeed.clear();
    own.remove();
  });

  it('keeps the dialog and its values when the host finds the answer invalid, and says so in its own words', async () => {
    answer = () => ({ status: 'invalid', message: 'Name is required' });
    feed.shown(notification({
      id: 'a',
      popup: 'dialog',
      content: form(),
      actions: [{ id: 'save', label: 'Save', style: 'primary' }],
    }));
    const input = root.querySelector('input') as HTMLInputElement;
    input.value = 'Ada';
    input.dispatchEvent(new Event('input'));

    buttons()[0].click();
    await settle();

    expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: 'save', values: { name: 'Ada' } }]);
    expect(visible()).toBeTrue();
    const problem = root.querySelector('.wc-notification-problem') as HTMLElement;
    expect(problem.getAttribute('role')).toBe('alert');
    expect(problem.textContent).toBe(english(ClientAppStrings.Feedback.Notification.Rejected));
    expect(problem.textContent).not.toContain('Name is required');
    expect(buttons()[0].disabled).toBeFalse();
    expect((root.querySelector('input') as HTMLInputElement).value).toBe('Ada');

    const field = root.querySelector('input') as HTMLInputElement;
    field.value = 'Ada L';
    field.dispatchEvent(new Event('blur'));

    expect(problem.hidden).toBeTrue();
  });

  it('clears the error once a later answer is accepted', async () => {
    answer = () => ({ status: 'invalid', message: 'x' });
    feed.shown(notification({ id: 'a', popup: 'dialog', actions: [{ id: 'save', label: 'Save', style: 'primary' }] }));
    buttons()[0].click();
    await settle();
    const problem = root.querySelector('.wc-notification-problem') as HTMLElement;
    expect(problem.hidden).toBeFalse();

    answer = () => ({ status: 'accepted' });
    buttons()[0].click();
    await settle();

    expect(problem.hidden).toBeTrue();
  });

  it('keeps text typed into the content when the host sends a new revision', () => {
    const first = notification({ id: 'a', popup: 'dialog', content: form(), progress: { processed: 1, total: 10 } });
    feed.shown(first);
    const input = root.querySelector('input') as HTMLInputElement;
    input.value = 'Ada';
    input.dispatchEvent(new Event('input'));

    feed.updated({ ...first, revision: 2, progress: { processed: 5, total: 10 } });

    expect((root.querySelector('input') as HTMLInputElement).value).toBe('Ada');
    expect(root.querySelector('[role="progressbar"]')!.getAttribute('aria-valuenow')).toBe('5');
  });

  it('offers close and Escape only when the host allows dismissing', async () => {
    feed.shown(notification({ id: 'a', popup: 'dialog', dismissible: false }));

    expect((root.querySelector('.wc-notification-close') as HTMLElement).hidden).toBeTrue();
    escape();
    await settle();
    expect(wire.calls.length).toBe(0);

    feed.updated({ ...feed.store.get('a')!, revision: 2, dismissible: true });
    escape();
    await settle();

    expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: null }]);
  });

  it('gives a dialog without actions an OK that dismisses it', async () => {
    feed.shown(notification({ id: 'a', popup: 'dialog' }));

    expect(buttons().length).toBe(1);
    buttons()[0].click();
    await settle();

    expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: null }]);
  });
});

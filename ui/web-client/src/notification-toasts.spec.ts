import { ClientAppStrings, LocalizationCatalog } from '@macro-deck/runtime';
import { NotificationFeed } from './notification-feed';
import { notification, recordingRequest, settle } from './notification-fixtures';
import { NotificationToasts } from './notification-toasts';
import { dismissAllToasts, mountToastHost, showToast } from './ui';

const catalog = new LocalizationCatalog();
const english = (qualifiedKey: string): string => {
  const separator = qualifiedKey.indexOf(':');
  return catalog.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1));
};

describe('notification toasts', () => {
  let root: HTMLElement;
  let feed: NotificationFeed;
  let wire: ReturnType<typeof recordingRequest>;

  const toasts = (): HTMLElement[] => Array.prototype.slice.call(root.querySelectorAll('.wc-toast'));
  const titles = (): string[] => toasts().map(toast => toast.querySelector('.wc-toast-message')!.textContent!);

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    mountToastHost(root, { dismissLabel: 'Dismiss' });
    wire = recordingRequest();
    feed = new NotificationFeed(wire.request);
    new NotificationToasts(feed, catalog);
  });

  afterEach(() => {
    feed.clear();
    dismissAllToasts();
    root.remove();
  });

  it('draws severity, message and the source the host derived in the one toast layer', () => {
    feed.shown(notification({
      severity: 'warning',
      title: 'Disk almost full',
      message: '2 GB left',
      sourceCategory: 'plugin',
      sourceName: 'Acme',
    }));

    const toast = toasts()[0];
    expect(toast.classList.contains('wc-toast-warning')).toBeTrue();
    expect(toast.querySelector('.wc-toast-detail')!.textContent).toBe('2 GB left');
    expect(toast.querySelector('.wc-toast-source-kind')!.textContent)
      .toBe(english(ClientAppStrings.Feedback.Notification.Source.Plugin));
    expect(toast.querySelector('.wc-toast-source-name')!.textContent).toBe('Acme');
    expect(toast.parentElement).toBe(root.querySelector('.wc-toasts'));
  });

  it('leaves the execution feedback toasts alone', () => {
    showToast('Failed to run', { kind: 'error' });
    feed.shown(notification({ title: 'From the host' }));

    expect(titles()).toEqual(['Failed to run', 'From the host']);
    expect(toasts()[0].classList.contains('wc-toast-error')).toBeTrue();
  });

  it('offers a dismiss control only when the host allows it, and dismisses through the host', async () => {
    feed.shown(notification({ id: 'a', title: 'Closable', dismissible: true }));
    feed.shown(notification({ id: 'b', title: 'Fixed', dismissible: false }));

    expect(toasts()[0].querySelector('.wc-toast-dismiss')).not.toBeNull();
    expect(toasts()[1].querySelector('.wc-toast-dismiss')).toBeNull();

    toasts()[0].querySelector<HTMLElement>('.wc-toast-dismiss')!.click();
    await settle();

    expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: null }]);
    expect(titles()).toEqual(['Closable', 'Fixed']);
  });

  it('answers with the action that was pressed', async () => {
    feed.shown(notification({
      id: 'a',
      actions: [
        { id: 'undo', label: 'Undo', style: 'primary' },
        { id: 'delete', label: 'Delete', style: 'danger' },
      ],
    }));

    const buttons = toasts()[0].querySelectorAll<HTMLElement>('.wc-toast-actions .wc-btn');
    expect(buttons[1].classList.contains('wc-btn-danger')).toBeTrue();
    buttons[0].click();
    await settle();

    expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: 'undo' }]);
  });

  it('shows progress as a progress bar', () => {
    feed.shown(notification({ progress: { processed: 3, total: 4 } }));

    const bar = toasts()[0].querySelector('[role="progressbar"]')!;
    expect(bar.getAttribute('aria-valuenow')).toBe('3');
    expect(bar.getAttribute('aria-valuemax')).toBe('4');
  });

  it('shows three at a time, oldest first, and promotes a queued one when a visible one closes', () => {
    for (const id of ['a', 'b', 'c', 'd']) feed.shown(notification({ id, title: id }));

    expect(titles()).toEqual(['a', 'b', 'c']);

    feed.closed({ id: 'a', revision: 2, reason: 'dismissed' });

    expect(titles()).toEqual(['b', 'c', 'd']);
  });

  it('forgets a queued popup the host closed before it was ever shown', () => {
    for (const id of ['a', 'b', 'c', 'd']) feed.shown(notification({ id, title: id }));

    feed.closed({ id: 'd', revision: 2, reason: 'timeout' });
    feed.closed({ id: 'a', revision: 2, reason: 'dismissed' });

    expect(titles()).toEqual(['b', 'c']);
  });

  it('repaints a popup in place when the host updates it', () => {
    const first = notification({ id: 'a', title: 'Copying', progress: { processed: 1, total: 4 } });
    feed.shown(first);
    feed.shown(notification({ id: 'b', title: 'Other' }));

    feed.updated({ ...first, revision: 2, title: 'Copied', progress: undefined });

    expect(titles()).toEqual(['Copied', 'Other']);
    expect(toasts()[0].querySelector('[role="progressbar"]')).toBeNull();
  });

  it('keeps announcing through the one polite live region', () => {
    const layer = root.querySelector('.wc-toasts')!;

    expect(layer.getAttribute('role')).toBe('status');
    expect(layer.getAttribute('aria-live')).toBe('polite');
  });

  it('shows nothing for a dialog or a popup the host asked to keep out of view', () => {
    feed.shown(notification({ popup: 'dialog' }));
    feed.shown(notification({ popup: 'none' }));

    expect(toasts().length).toBe(0);
  });
});

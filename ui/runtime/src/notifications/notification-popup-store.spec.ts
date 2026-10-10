import { ActiveNotification } from '../protocol/messages/notifications';
import { NotificationPopupStore } from './notification-popup-store';

function notification(id: string, revision = 0, overrides: Partial<ActiveNotification> = {}): ActiveNotification {
  return {
    id,
    revision,
    sourceCategory: 'plugin',
    sourceId: 'com.example',
    sourceName: 'Example',
    title: id,
    severity: 'info',
    popup: 'toast',
    inbox: false,
    dismissible: true,
    createdAt: new Date(1_000 + Number(id.replace(/\D/g, '') || 0)).toISOString(),
    actions: [],
    ...overrides,
  };
}

describe('NotificationPopupStore', () => {
  let now = 1_000_000;
  let store: NotificationPopupStore;

  beforeEach(() => {
    now = 1_000_000;
    store = new NotificationPopupStore(() => now);
  });

  it('removes a popup the host closed, also while it is queued behind others', () => {
    store.shown(notification('n1'));
    store.shown(notification('n2'));
    store.shown(notification('n3'));

    store.closed({ id: 'n2', revision: 1, reason: 'action', actionId: 'ok' });

    expect(store.list().map(n => n.id)).toEqual(['n1', 'n3']);
  });

  it('ignores an update that is not newer than what it already holds', () => {
    store.shown(notification('n1', 3, { title: 'new' }));

    store.updated(notification('n1', 2, { title: 'stale' }));
    store.updated(notification('n1', 3, { title: 'same revision' }));

    expect(store.get('n1')?.title).toBe('new');
  });

  it('does not bring a closed popup back from a late event or a late snapshot', () => {
    store.shown(notification('n1', 0));
    store.closed({ id: 'n1', revision: 1, reason: 'dismissed' });

    store.shown(notification('n1', 0));
    store.replace([notification('n1', 0)]);

    expect(store.list()).toEqual([]);
  });

  it('forgets a closed id after the tombstone lifetime', () => {
    store.closed({ id: 'n1', revision: 1, reason: 'dismissed' });
    now += 6 * 60 * 1000;

    store.shown(notification('n1', 5));

    expect(store.list().map(n => n.id)).toEqual(['n1']);
  });

  it('treats a snapshot as authoritative: popups it does not list are dropped', () => {
    store.shown(notification('n1'));
    store.shown(notification('n2'));

    store.replace([notification('n2', 1), notification('n3')]);

    expect(store.list().map(n => n.id)).toEqual(['n2', 'n3']);
    expect(store.get('n2')?.revision).toBe(1);
  });

  it('applies the host expiry to popups that were never shown yet', () => {
    store.shown(notification('n1', 0, { expiresAt: new Date(now + 1000).toISOString() }));
    store.shown(notification('n2', 0, { expiresAt: new Date(now + 60_000).toISOString() }));

    now += 2000;
    const expired = store.expireDue();

    expect(expired).toEqual(['n1']);
    expect(store.list().map(n => n.id)).toEqual(['n2']);
    expect(store.nextExpiry()).toBe(now - 2000 + 60_000);
  });

  it('counts the time left from receipt, so a host clock that is far off does not drop a popup early', () => {
    store.shown(notification('n1', 0, {
      expiresAt: new Date(now - 3_600_000).toISOString(),
      expiresInMs: 5000,
    }));

    now += 4000;
    expect(store.expireDue()).toEqual([]);
    now += 2000;
    expect(store.expireDue()).toEqual(['n1']);
  });

  it('keeps popups without a popup kind out of the visible list', () => {
    store.shown(notification('n1', 0, { popup: 'none' }));

    expect(store.list()).toEqual([]);
  });

  it('tells subscribers once per change and stops after unsubscribe', () => {
    let calls = 0;
    const unsubscribe = store.subscribe(() => calls++);

    store.shown(notification('n1'));
    store.closed({ id: 'n1', revision: 1, reason: 'timeout' });
    unsubscribe();
    store.shown(notification('n2'));

    expect(calls).toBe(2);
  });
});

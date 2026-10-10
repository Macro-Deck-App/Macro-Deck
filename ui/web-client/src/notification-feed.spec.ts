import { Client } from './client';
import { NotificationFeed } from './notification-feed';
import { notification, recordingRequest, settle } from './notification-fixtures';

const deliver = (client: Client, type: string, body: unknown): void =>
  (client as unknown as { onNotification(type: string, payload: unknown): void }).onNotification(type, body);

describe('notification feed', () => {
  describe('on the client', () => {
    let client: Client;

    beforeEach(() => {
      client = new Client(() => 'http://host', 'client-1');
    });

    it('takes shown, updated and closed events into one store', () => {
      const first = notification({ id: 'a', title: 'Old' });
      deliver(client, 'NotificationShownEvent', { notification: first });
      deliver(client, 'NotificationUpdatedEvent', { notification: { ...first, revision: 2, title: 'New' } });

      expect(client.notifications.store.get('a')?.title).toBe('New');

      deliver(client, 'NotificationClosedEvent', { id: 'a', revision: 3, reason: 'programmatic' });

      expect(client.notifications.store.get('a')).toBeUndefined();
    });

    it('treats the snapshot as the whole truth after a reconnect', () => {
      deliver(client, 'NotificationShownEvent', { notification: notification({ id: 'gone' }) });
      deliver(client, 'NotificationShownEvent', { notification: notification({ id: 'kept' }) });

      deliver(client, 'ActiveNotificationsEvent', { notifications: [notification({ id: 'kept' }), notification({ id: 'new' })] });

      expect(client.notifications.store.list().map(n => n.id).sort()).toEqual(['kept', 'new']);
    });

    it('stays inert when the host never sends any', () => {
      deliver(client, 'FolderCreatedEvent', {});

      expect(client.notifications.store.list()).toEqual([]);
    });
  });

  describe('responding', () => {
    it('sends the id, the chosen action and the form values as the one positional argument', async () => {
      const wire = recordingRequest();
      const feed = new NotificationFeed(wire.request);
      feed.shown(notification({ id: 'a' }));

      await feed.respond('a', 'save', { name: 'Ada' });

      expect(wire.calls).toEqual([
        { type: 'RespondToNotification', payload: [{ id: 'a', actionId: 'save', values: { name: 'Ada' } }] },
      ]);
    });

    it('sends a null action to dismiss', async () => {
      const wire = recordingRequest();
      const feed = new NotificationFeed(wire.request);

      await feed.respond('a', null);

      expect(wire.calls[0].payload).toEqual([{ id: 'a', actionId: null }]);
    });

    it('keeps the popup after a refusal, since only the host closes it', async () => {
      const feed = new NotificationFeed(recordingRequest(() => ({ status: 'invalid', message: 'Name is required' })).request);
      feed.shown(notification({ id: 'a' }));

      const response = await feed.respond('a', 'save');

      expect(response?.status).toBe('invalid');
      expect(feed.store.get('a')).toBeDefined();
    });

    it('drops a popup the host reports as already closed, because no closed event will follow', async () => {
      const feed = new NotificationFeed(recordingRequest(() => ({ status: 'already-closed' })).request);
      feed.shown(notification({ id: 'a' }));

      await feed.respond('a', null);

      expect(feed.store.get('a')).toBeUndefined();
    });

    it('does not send a second response while one is in flight', async () => {
      const wire = recordingRequest();
      const feed = new NotificationFeed(wire.request);

      void feed.respond('a', 'x');
      void feed.respond('a', 'x');
      await settle();

      expect(wire.calls.length).toBe(1);
    });
  });

  describe('expiry', () => {
    beforeEach(() => {
      jasmine.clock().install();
      jasmine.clock().mockDate(new Date(0));
    });

    afterEach(() => jasmine.clock().uninstall());

    it('removes a popup at the time the host set, queued or not', () => {
      const feed = new NotificationFeed(recordingRequest().request);
      feed.shown(notification({ id: 'soon', expiresAt: new Date(2000).toISOString() }));
      feed.shown(notification({ id: 'later', expiresAt: new Date(9000).toISOString() }));

      jasmine.clock().tick(1999);
      expect(feed.store.get('soon')).toBeDefined();

      jasmine.clock().tick(1);
      expect(feed.store.get('soon')).toBeUndefined();
      expect(feed.store.get('later')).toBeDefined();

      jasmine.clock().tick(7000);
      expect(feed.store.get('later')).toBeUndefined();
    });

    it('follows the clock it was given instead of the device clock', () => {
      const feed = new NotificationFeed(recordingRequest().request);
      feed.useClock(() => Date.now() + 5000);
      feed.shown(notification({ id: 'a', expiresAt: new Date(6000).toISOString() }));

      jasmine.clock().tick(1000);

      expect(feed.store.get('a')).toBeUndefined();
    });
  });
});

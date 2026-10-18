import { Client } from './client';
import { BlockedLinkBanner, LinkOpener } from './link-opener';
import { mountToastHost } from './ui';

describe('links the host opens on this device', () => {
  const notify = (target: Client, type: string, body: unknown) =>
    (target as unknown as { onNotification(type: string, payload: unknown): void }).onNotification(type, body);

  it('declares to the host that this client can open links', () => {
    const client = new Client(() => 'http://host', 'client-1');

    const options = (client.connection as unknown as { options: { features?: readonly string[] } }).options;

    expect(options.features).toEqual(['open-url']);
  });

  it('opens a web link in a new tab that cannot reach back to the deck', () => {
    const tab = { opener: {} as unknown } as Window;
    const openWindow = jasmine.createSpy('openWindow').and.returnValue(tab);
    const client = new Client(() => 'http://host', 'client-1', { openWindow });

    notify(client, 'UiOpenUrlEvent', { url: 'https://example.com/article' });

    expect(openWindow).toHaveBeenCalledOnceWith('https://example.com/article');
    expect(tab.opener).toBeNull();
    expect(client.links.blocked.get()).toBeNull();
  });

  it('ignores a link that is not a web address', () => {
    const openWindow = jasmine.createSpy('openWindow');
    const client = new Client(() => 'http://host', 'client-1', { openWindow });

    notify(client, 'UiOpenUrlEvent', { url: 'javascript:alert(1)' });
    notify(client, 'UiOpenUrlEvent', { url: 'data:text/html,hi' });
    notify(client, 'UiOpenUrlEvent', {});

    expect(openWindow).not.toHaveBeenCalled();
    expect(client.links.blocked.get()).toBeNull();
  });

  describe('when the browser blocks the new tab', () => {
    let root: HTMLElement;
    let links: LinkOpener;

    beforeEach(() => {
      jasmine.clock().install();
      root = document.createElement('div');
      document.body.appendChild(root);
      mountToastHost(root, { dismissLabel: 'Dismiss' });
      links = new LinkOpener(() => null);
      new BlockedLinkBanner(links, key => key);
    });

    afterEach(() => {
      links.dismiss();
      jasmine.clock().uninstall();
      root.remove();
    });

    it('offers the link to tap instead, without a referrer or a way back', () => {
      links.open('https://example.com/article');

      const anchor = root.querySelector<HTMLAnchorElement>('.wc-toast a.wc-toast-link');
      expect(anchor).not.toBeNull();
      expect(anchor!.getAttribute('href')).toBe('https://example.com/article');
      expect(anchor!.target).toBe('_blank');
      expect(anchor!.rel.split(' ').sort()).toEqual(['noopener', 'noreferrer']);
    });

    it('takes the offer down once the link was tapped', () => {
      links.open('https://example.com/article');

      const anchor = root.querySelector<HTMLAnchorElement>('a.wc-toast-link')!;
      anchor.addEventListener('click', event => event.preventDefault());
      anchor.click();
      jasmine.clock().tick(1);

      expect(root.querySelector('a.wc-toast-link')).toBeNull();
    });
  });
});

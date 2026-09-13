import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConnectionPanelComponent, encodeConnectLink } from './connection-panel.component';
import { EMPTY } from 'rxjs';
import { create } from 'qrcode';

describe('ConnectionPanelComponent', () => {
  let fixture: ComponentFixture<ConnectionPanelComponent>;
  let api: jasmine.SpyObj<ApiService>;

  const address = '192.168.1.10';
  const http: ConnectionEndpoint = { address, port: 8193, ssl: false };
  const https: ConnectionEndpoint = { address, port: 8194, ssl: true };

  const info: GetConnectionInfoResponse = {
    instanceName: 'Test Instance',
    endpoints: [http],
    publicListenerUnavailable: false,
    version: '3.0.0-test',
  };

  const pairingCode = { code: '482915', expiresAt: new Date(Date.now() + 15 * 60_000).toISOString() };

  async function settle(target: ComponentFixture<ConnectionPanelComponent>): Promise<void> {
    for (let i = 0; i < 10; i++) {
      await target.whenStable();
    }
    target.detectChanges();
  }

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getConnectionInfo', 'onNotification', 'rotatePairingCode', 'getPairingCode']);
    api.getConnectionInfo.and.resolveTo(info);
    api.rotatePairingCode.and.resolveTo(pairingCode);
    api.getPairingCode.and.resolveTo(pairingCode);
    api.onNotification.and.returnValue(EMPTY);

    await TestBed.configureTestingModule({
      imports: [ConnectionPanelComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();

    fixture = TestBed.createComponent(ConnectionPanelComponent);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await settle(fixture);
  });

  async function renderPanel(overrides: Partial<GetConnectionInfoResponse> = {}): Promise<HTMLElement> {
    api.getConnectionInfo.and.resolveTo({ ...info, ...overrides });
    const panel = TestBed.createComponent(ConnectionPanelComponent);
    panel.componentRef.setInput('isOpen', true);
    panel.detectChanges();
    await settle(panel);
    return panel.nativeElement as HTMLElement;
  }

  it('loads nothing until it is opened, and reloads on every open', async () => {
    const closedFixture = TestBed.createComponent(ConnectionPanelComponent);
    closedFixture.detectChanges();
    await closedFixture.whenStable();
    const callsBefore = api.getConnectionInfo.calls.count();

    closedFixture.componentRef.setInput('isOpen', true);
    closedFixture.detectChanges();
    await closedFixture.whenStable();

    expect(api.getConnectionInfo.calls.count()).toBe(callsBefore + 1);
  });

  it('stays in the DOM while closed, collapsed and inert', async () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
    await fixture.whenStable();

    const panel = fixture.nativeElement.querySelector('.cp-panel') as HTMLElement;
    expect(panel).toBeTruthy();
    expect(panel.classList).not.toContain('cp-panel-open');
    expect(panel.hasAttribute('inert')).toBeTrue();
  });

  it('expands the target picker only for the clicked endpoint, and collapses on re-click', () => {
    const component = fixture.componentInstance;
    expect(component['expanded']()).toBeNull();

    component.toggle(http);
    expect(component['expanded']()).toBe('http://192.168.1.10:8193');

    component.toggle(http);
    expect(component['expanded']()).toBeNull();
  });

  // An address can now appear twice, so expanding one row must not expand its sibling.
  it('expands one endpoint of an address without expanding the other', async () => {
    const element = await renderPanel({ endpoints: [https, http] });
    const rows = element.querySelectorAll('.cp-interface');
    (rows[0] as HTMLElement).click();
    fixture.detectChanges();

    const groups = element.querySelectorAll('.cp-interface-group');
    expect(groups[0].classList).toContain('cp-interface-group-open');
    expect(groups[1].classList).not.toContain('cp-interface-group-open');
  });

  it('opens the web client at the root for the client target and collapses the picker', () => {
    const openSpy = spyOn(window, 'open');
    fixture.componentInstance.toggle(http);

    fixture.componentInstance.open(http, 'client');

    expect(openSpy).toHaveBeenCalledWith('http://192.168.1.10:8193', '_blank', 'noopener,noreferrer');
    expect(fixture.componentInstance['expanded']()).toBeNull();
  });

  it('opens the configuration UI under /admin for the config target', () => {
    const openSpy = spyOn(window, 'open');

    fixture.componentInstance.open(http, 'config');

    expect(openSpy).toHaveBeenCalledWith('http://192.168.1.10:8193/admin', '_blank', 'noopener,noreferrer');
  });

  it('routes to the default browser via the shell bridge when available', () => {
    const openExternal = jasmine.createSpy('openExternal').and.resolveTo(true);
    (window as unknown as { macroDeckShell?: unknown }).macroDeckShell = { openExternal };
    const openSpy = spyOn(window, 'open');

    try {
      fixture.componentInstance.open(http, 'client');

      expect(openExternal).toHaveBeenCalledWith('http://192.168.1.10:8193');
      expect(openSpy).not.toHaveBeenCalled();
    } finally {
      delete (window as unknown as { macroDeckShell?: unknown }).macroDeckShell;
    }
  });

  it('does not render the host version', () => {
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('3.0.0-test');
  });

  it('renders both targets once an endpoint is expanded', () => {
    fixture.componentInstance.toggle(http);
    fixture.detectChanges();

    const targets = (fixture.nativeElement as HTMLElement).querySelectorAll('.cp-target');
    expect(targets.length).toBe(2);
    expect(targets[0].textContent).toContain('Configuration UI');
    expect(targets[1].textContent).toContain('Web Client');
  });

  it('emits closed when the panel header close button is clicked', () => {
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.closed.subscribe(closed);

    (fixture.nativeElement.querySelector('.cp-close') as HTMLElement).click();

    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('renders neither the QR code nor the address list when the public listener is unavailable', async () => {
    const element = await renderPanel({ publicListenerUnavailable: true, endpoints: [] });

    expect(element.querySelector('img[alt="Connect QR code"]')).toBeNull();
    expect(element.textContent).not.toContain('http://192.168.1.10:8193');
    expect(element.textContent).toContain('not reachable on the network');
  });

  it('still renders the QR code and address list when the public listener is healthy', async () => {
    const element = await renderPanel();

    expect(element.querySelector('img[alt="Connect QR code"]')).not.toBeNull();
    expect(element.textContent).toContain('http://192.168.1.10:8193');
  });

  // The host decides the scheme per endpoint; the panel must not assume one for the whole host.
  it('renders each configured endpoint with its own scheme', async () => {
    const element = await renderPanel({ endpoints: [https, http] });
    const urls = Array.from(element.querySelectorAll('.cp-interface-url')).map((node) => node.textContent?.trim());

    expect(urls).toEqual(['https://192.168.1.10:8194', 'http://192.168.1.10:8193']);
  });

  it('renders only the https endpoint when https replaced the http listener', async () => {
    const element = await renderPanel({ endpoints: [{ address, port: 8193, ssl: true }] });
    const urls = Array.from(element.querySelectorAll('.cp-interface-url')).map((node) => node.textContent?.trim());

    expect(urls).toEqual(['https://192.168.1.10:8193']);
    expect(element.textContent).not.toContain('http://192.168.1.10');
  });

  it('marks an https endpoint with a closed lock and an http endpoint with an open one', async () => {
    const element = await renderPanel({ endpoints: [https, http] });
    const rows = Array.from(element.querySelectorAll('.cp-interface'));

    const locks = rows.map((row) => row.querySelector('.cp-interface-lock') as HTMLElement);
    expect(locks.length).toBe(2);

    expect(locks[0].classList).toContain('icon-lock');
    expect(locks[0].classList).not.toContain('icon-unlock');
    expect(locks[0].getAttribute('aria-label')).toBe('Encrypted connection');

    expect(locks[1].classList).toContain('icon-unlock');
    expect(locks[1].classList).not.toContain('icon-lock');
    expect(locks[1].getAttribute('aria-label')).toBe('Unencrypted connection');
  });

  it('indicates the scheme per address without the recommended or ssl badges', async () => {
    const element = await renderPanel({ endpoints: [https, http] });

    expect(element.textContent).not.toContain('Recommended');
    expect(element.textContent).not.toContain('SSL enabled');
    expect(element.textContent).not.toContain('SSL disabled');
  });

  it('uses https to open an ssl endpoint', () => {
    const openSpy = spyOn(window, 'open');

    fixture.componentInstance.open(https, 'client');

    expect(openSpy).toHaveBeenCalledWith('https://192.168.1.10:8194', '_blank', 'noopener,noreferrer');
  });

  describe('connect link v3', () => {
    const prefix = 'https://connect.macro-deck.app/';
    const referenceHost: GetConnectionInfoResponse = {
      ...info,
      instanceName: 'Companion test host',
      endpoints: [http, https],
    };

    function linkBytes(url: string): number[] {
      const digits = url.slice(prefix.length);
      const bytes: number[] = [];
      for (let i = 0; i < digits.length; i += 5) {
        const value = Number(digits.slice(i, i + 5));
        bytes.push(...(digits.length - i === 3 ? [value] : [value >> 8, value & 0xff]));
      }
      return bytes;
    }

    it('matches the conformance vector in engineering/api/connect-link.md', () => {
      expect(encodeConnectLink(referenceHost, '482915')).toBe(prefix +
        '00787172632801624942269912819229797295560829628531296980019243009025920025600192430090259200513015881438614641053');
    });

    it('fits a far smaller QR code than the version 2 link did', () => {
      expect(create(encodeConnectLink(referenceHost, '482915'), { errorCorrectionLevel: 'L' }).version)
        .toBeLessThanOrEqual(5);
    });

    it('writes hostnames as text and skips addresses it cannot describe, with an empty token', () => {
      const link = encodeConnectLink({
        ...info,
        instanceName: 'H',
        endpoints: [
          { address: 'fe80::1', port: 8193, ssl: false },
          { address: 'deck.local', port: 8194, ssl: true },
          { address: '300.1.1.1', port: 8193, ssl: false },
          { address: 'a'.repeat(256), port: 8193, ssl: false },
        ],
      }, '');

      expect(link).toMatch(/^https:\/\/connect\.macro-deck\.app\/\d+$/);
      expect(linkBytes(link)).toEqual([
        3, 1, 0x48, 1,
        2, 10, ...Array.from('deck.local', (c) => c.charCodeAt(0)), 0x20, 0x02, 1,
        0,
      ]);
    });

    it('cuts an overlong instance name on a character boundary', () => {
      const bytes = linkBytes(encodeConnectLink({ ...info, instanceName: 'ü'.repeat(200), endpoints: [] }, ''));

      expect(bytes[1]).toBe(254);
      expect(new TextDecoder('utf-8', { fatal: true }).decode(new Uint8Array(bytes.slice(2, 2 + bytes[1]))))
        .toBe('ü'.repeat(127));
    });
  });
  it('puts the code minted on open into the connect payload and shows it grouped', async () => {
    const buildConnectUrl = spyOn(
      ConnectionPanelComponent.prototype as unknown as { buildConnectUrl: (...args: unknown[]) => string },
      'buildConnectUrl'
    ).and.callThrough();
    const rotationsBefore = api.rotatePairingCode.calls.count();

    const element = await renderPanel();

    expect(api.rotatePairingCode.calls.count()).toBe(rotationsBefore + 1);
    expect(buildConnectUrl.calls.mostRecent().args[1]).toBe('482915');
    expect(element.querySelector('.cp-pairing-code')?.textContent?.trim()).toBe('482 915');
    expect(element.textContent).toContain('Expires in');
  });

  it('replaces the code on every open', async () => {
    const panel = TestBed.createComponent(ConnectionPanelComponent);
    panel.detectChanges();
    await panel.whenStable();
    const before = api.rotatePairingCode.calls.count();

    for (const open of [true, false, true]) {
      panel.componentRef.setInput('isOpen', open);
      panel.detectChanges();
      await panel.whenStable();
    }

    expect(api.rotatePairingCode.calls.count()).toBe(before + 2);
  });

  it('keeps a single poller when the panel is reopened before the first load finished', async () => {
    const started = spyOn(window, 'setInterval').and.callThrough();
    const stopped = spyOn(window, 'clearInterval').and.callThrough();
    const panel = TestBed.createComponent(ConnectionPanelComponent);

    for (const open of [true, false, true]) {
      panel.componentRef.setInput('isOpen', open);
      panel.detectChanges();
    }
    await settle(panel);

    expect(started.calls.count() - stopped.calls.count()).toBe(1);
    panel.destroy();
  });

  it('hides the code and keeps an empty token when the host refuses to hand one out', async () => {
    api.rotatePairingCode.and.rejectWith(new Error('403'));
    const buildConnectUrl = spyOn(
      ConnectionPanelComponent.prototype as unknown as { buildConnectUrl: (...args: unknown[]) => string },
      'buildConnectUrl'
    ).and.callThrough();

    const element = await renderPanel();

    expect(element.querySelector('.cp-pairing-code')).toBeNull();
    expect(element.querySelector('img[alt="Connect QR code"]')).not.toBeNull();
    expect(buildConnectUrl.calls.mostRecent().args[1]).toBe('');
  });

  it('shows the new code once the host replaced it', async () => {
    jasmine.clock().install();
    try {
      const panel = TestBed.createComponent(ConnectionPanelComponent);
      panel.componentRef.setInput('isOpen', true);
      panel.detectChanges();
      await settle(panel);
      api.getPairingCode.and.resolveTo({ ...pairingCode, code: '107233' });

      jasmine.clock().tick(5_000);
      await settle(panel);

      const text = (panel.nativeElement as HTMLElement).querySelector('.cp-pairing-code')?.textContent?.trim();
      expect(text).toBe('107 233');
    } finally {
      jasmine.clock().uninstall();
    }
  });
});

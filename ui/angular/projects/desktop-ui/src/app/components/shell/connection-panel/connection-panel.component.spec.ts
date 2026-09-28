import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConnectionPanelComponent } from './connection-panel.component';
import { ConnectQrComponent } from '../connect-qr/connect-qr.component';
import { EMPTY } from 'rxjs';

@Component({
  standalone: true,
  imports: [ConnectionPanelComponent],
  template: `
    <div style="position: fixed; top: 0; left: 0; width: 1000px; height: 400px">
      <app-connection-panel [isOpen]="true" />
    </div>
  `,
})
class ConnectionHostComponent {}

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

  async function settle(target: ComponentFixture<unknown>): Promise<void> {
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

    const panel = fixture.nativeElement.querySelector('.sp-panel') as HTMLElement;
    expect(panel).toBeTruthy();
    expect(panel.classList).not.toContain('sp-panel-open');
    expect(panel.hasAttribute('inert')).toBeTrue();
  });

  it('keeps its 17.5rem width and opens from the end edge', async () => {
    const host = TestBed.createComponent(ConnectionHostComponent);
    host.detectChanges();
    await settle(host);

    const stage = host.nativeElement.firstElementChild as HTMLElement;
    const panel = host.nativeElement.querySelector('.sp-panel') as HTMLElement;
    panel.getAnimations().forEach(animation => animation.finish());
    const rootFontSizePx = parseFloat(getComputedStyle(document.documentElement).fontSize);
    const rect = panel.getBoundingClientRect();

    expect(rect.width).toBeCloseTo(17.5 * rootFontSizePx, 0);
    expect(rect.right).toBeCloseTo(stage.getBoundingClientRect().right, 0);
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

    (fixture.nativeElement.querySelector('.sp-close') as HTMLElement).click();

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

  it('puts the code minted on open into the connect payload and shows it grouped', async () => {
    const buildConnectUrl = spyOn(
      ConnectQrComponent.prototype as unknown as { buildConnectUrl: (...args: unknown[]) => string },
      'buildConnectUrl'
    ).and.callThrough();
    const rotationsBefore = api.rotatePairingCode.calls.count();

    const element = await renderPanel();

    expect(api.rotatePairingCode.calls.count()).toBe(rotationsBefore + 1);
    expect(buildConnectUrl.calls.mostRecent().args[1]).toBe('482915');
    expect(element.querySelector('.cq-pairing-code')?.textContent?.trim()).toBe('482 915');
    expect(element.textContent).toContain('Expires in');
  });

  it('replaces the code on every open', async () => {
    const panel = TestBed.createComponent(ConnectionPanelComponent);
    panel.detectChanges();
    await panel.whenStable();
    const before = api.rotatePairingCode.calls.count();

    for (const open of [true, false, true]) {
      panel.componentRef.setInput('isOpen', open);
      await settle(panel);
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

  it('stops asking the host for the pairing code once it is closed', async () => {
    jasmine.clock().install();
    try {
      const panel = TestBed.createComponent(ConnectionPanelComponent);
      panel.componentRef.setInput('isOpen', true);
      panel.detectChanges();
      await settle(panel);
      panel.componentRef.setInput('isOpen', false);
      await settle(panel);
      const polls = api.getPairingCode.calls.count();

      jasmine.clock().tick(30_000);
      await settle(panel);

      expect(api.getPairingCode.calls.count()).toBe(polls);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides the code and keeps an empty token when the host refuses to hand one out', async () => {
    api.rotatePairingCode.and.rejectWith(new Error('403'));
    const buildConnectUrl = spyOn(
      ConnectQrComponent.prototype as unknown as { buildConnectUrl: (...args: unknown[]) => string },
      'buildConnectUrl'
    ).and.callThrough();

    const element = await renderPanel();

    expect(element.querySelector('.cq-pairing-code')).toBeNull();
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

      const text = (panel.nativeElement as HTMLElement).querySelector('.cq-pairing-code')?.textContent?.trim();
      expect(text).toBe('107 233');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the host identity fingerprint next to the pairing code', async () => {
    const element = await renderPanel({ identityFingerprint: '3208 E004 6ED3 EE6B 4E75 1027' });

    expect(element.querySelector('.cp-identity .cp-fingerprint')?.textContent?.trim())
      .toBe('3208 E004 6ED3 EE6B 4E75 1027');
  });

  it('says the identity is unavailable when the host cannot load its key', async () => {
    const element = await renderPanel({ identityFingerprint: null });

    expect(element.querySelector('.cp-identity .cp-fingerprint')).toBeNull();
    expect(element.querySelector('.cp-identity .cp-muted')).not.toBeNull();
  });
});

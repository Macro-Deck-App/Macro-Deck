import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConnectionPanelComponent } from './connection-panel.component';
import { EMPTY } from 'rxjs';

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

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getConnectionInfo', 'onNotification']);
    api.getConnectionInfo.and.resolveTo(info);
    api.onNotification.and.returnValue(EMPTY);

    await TestBed.configureTestingModule({
      imports: [ConnectionPanelComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();

    fixture = TestBed.createComponent(ConnectionPanelComponent);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  async function renderPanel(overrides: Partial<GetConnectionInfoResponse> = {}): Promise<HTMLElement> {
    api.getConnectionInfo.and.resolveTo({ ...info, ...overrides });
    const panel = TestBed.createComponent(ConnectionPanelComponent);
    panel.componentRef.setInput('isOpen', true);
    panel.detectChanges();
    await panel.whenStable();
    await panel.whenStable();
    panel.detectChanges();
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

  it('encodes every endpoint with its own ssl flag in the connect payload', async () => {
    const panel = TestBed.createComponent(ConnectionPanelComponent);
    api.getConnectionInfo.and.resolveTo({ ...info, endpoints: [https, http] });
    panel.componentRef.setInput('isOpen', true);
    panel.detectChanges();
    await panel.whenStable();

    const url = panel.componentInstance['buildConnectUrl']({ ...info, endpoints: [https, http] });
    const payload = JSON.parse(atob(url.replace('https://connect.macro-deck.app/', ''))) as {
      payloadVersion: number;
      endpoints: ConnectionEndpoint[];
    };

    expect(payload.payloadVersion).toBe(2);
    expect(payload.endpoints).toEqual([https, http]);
  });
});

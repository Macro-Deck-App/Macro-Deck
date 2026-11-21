import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConnectionDiagnosticsService, ConnectionReport } from './connection-diagnostics.service';

describe('ConnectionDiagnosticsService', () => {
  let service: ConnectionDiagnosticsService;
  let reports: ConnectionReport[];

  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date('2026-11-19T12:00:00Z'));
    reports = [];
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      reportUiConnection: (report: ConnectionReport) => {
        reports.push(report);
        return Promise.resolve();
      },
    };
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    service = TestBed.inject(ConnectionDiagnosticsService);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  it('reports the first failure of a step with its reason', () => {
    service.attempting('ticket');
    service.fail('ticket', new Error('UI WebSocket ticket request failed: 500'));

    expect(reports).toEqual([
      { step: 'ticket', reason: 'UI WebSocket ticket request failed: 500', attempt: 1, recovered: false },
    ]);
  });

  it('does not repeat the same failure every retry, but reports a new step and a reminder later', () => {
    service.fail('socket', new Error('WebSocket connection failed.'));
    jasmine.clock().tick(2000);
    service.fail('socket', new Error('WebSocket connection failed.'));
    expect(reports.length).toBe(1);

    jasmine.clock().tick(2000);
    service.fail('ticket', new Error('UI WebSocket ticket request failed: 500'));
    expect(reports.map(report => report.step)).toEqual(['socket', 'ticket']);

    jasmine.clock().tick(30_000);
    service.fail('ticket', new Error('UI WebSocket ticket request failed: 500'));
    expect(reports.map(report => report.attempt)).toEqual([1, 3, 4]);
  });

  it('stalls after 15 seconds without a connection, even when no step ever failed', () => {
    service.attempting('key-ring');
    jasmine.clock().tick(14_999);
    expect(service.stalled()).toBeFalse();

    jasmine.clock().tick(1);

    expect(service.stalled()).toBeTrue();
    expect(reports).toEqual([{ step: 'key-ring', reason: 'no answer yet', attempt: 0, recovered: false }]);
  });

  it('clears the stall and reports the recovery once the window connects', () => {
    service.fail('socket', new Error('WebSocket connection failed.'));
    jasmine.clock().tick(15_000);
    expect(service.stalled()).toBeTrue();

    service.connectedNow();

    expect(service.stalled()).toBeFalse();
    expect(reports.at(-1)).toEqual({ step: 'socket', reason: '', attempt: 1, recovered: true });
  });

  it('stays quiet about reconnects after the window has connected once', () => {
    service.attempting('host-url');
    service.connectedNow();

    service.fail('socket', new Error('WebSocket closed before the handshake completed.'));
    jasmine.clock().tick(60_000);

    expect(reports).toEqual([]);
    expect(service.stalled()).toBeFalse();
  });

  it('neither stalls nor reports while the window waits on the user or an update', () => {
    service.attempting('auth');
    jasmine.clock().tick(10_000);
    service.setActive(false);

    service.fail('socket', new Error('WebSocket connection failed.'));
    jasmine.clock().tick(60_000);

    expect(service.stalled()).toBeFalse();
    expect(reports).toEqual([]);
  });

  it('starts a fresh 15 seconds once the window is connecting again', () => {
    service.attempting('auth');
    jasmine.clock().tick(10_000);
    service.setActive(false);
    service.setActive(true);

    service.attempting('host-url');
    jasmine.clock().tick(14_999);
    expect(service.stalled()).toBeFalse();
    jasmine.clock().tick(1);

    expect(service.stalled()).toBeTrue();
    expect(reports).toEqual([{ step: 'host-url', reason: 'no answer yet', attempt: 0, recovered: false }]);
  });
});

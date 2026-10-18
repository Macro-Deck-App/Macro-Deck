import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';

import type { UiSessionHandle } from '@shared';
import { ACTION_CONFIG_FLUSH_TIMEOUT_MS, ActionConfigSessionFlusher } from './action-config-session-flusher.service';

function sessionFlushing(flush: () => Promise<void>): UiSessionHandle {
  return {
    root: signal(null),
    revision: signal(0),
    rejection: signal(null),
    generation: signal(0),
    send: () => undefined,
    close: () => undefined,
    flush,
  };
}

describe('ActionConfigSessionFlusher', () => {
  let flusher: ActionConfigSessionFlusher;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    flusher = TestBed.inject(ActionConfigSessionFlusher);
  });

  afterEach(() => jasmine.clock().uninstall());

  it('waits until every open session confirmed its flush', async () => {
    let confirm!: () => void;
    flusher.register(sessionFlushing(() => new Promise<void>(resolve => confirm = resolve)));
    flusher.register(sessionFlushing(() => Promise.resolve()));
    let done = false;

    const flushing = flusher.flushAll().then(() => done = true);
    await Promise.resolve();
    expect(done).toBeFalse();

    confirm();
    await flushing;
    expect(done).toBeTrue();
  });

  it('gives up on a session that never confirms, warns, and lets the save go ahead', async () => {
    jasmine.clock().install();
    const warn = spyOn(console, 'warn');
    flusher.register(sessionFlushing(() => new Promise<void>(() => undefined)));

    const flushing = flusher.flushAll();
    jasmine.clock().tick(ACTION_CONFIG_FLUSH_TIMEOUT_MS);
    await flushing;

    expect(warn).toHaveBeenCalledTimes(1);
  });
});

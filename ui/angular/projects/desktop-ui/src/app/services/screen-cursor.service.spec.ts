import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ShellCursorPosition } from '../util/shell-bridge';
import { SCREEN_CURSOR_POLL_MS, ScreenCursorService } from './screen-cursor.service';

describe('ScreenCursorService', () => {
  let getCursorPosition: jasmine.Spy<() => Promise<ShellCursorPosition | null>>;
  let clock: jasmine.Clock;
  let hidden: boolean;

  function createService(withBridge = true): ScreenCursorService {
    if (withBridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = { getCursorPosition };
    }
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    return TestBed.inject(ScreenCursorService);
  }

  async function drain(): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  }

  async function advance(ticks = 1): Promise<void> {
    for (let i = 0; i < ticks; i++) {
      clock.tick(SCREEN_CURSOR_POLL_MS);
      await drain();
    }
  }

  beforeEach(() => {
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    getCursorPosition = jasmine.createSpy('getCursorPosition').and.resolveTo({ x: 1920, y: 540 });
    clock = jasmine.clock();
    clock.install();
  });

  afterEach(() => {
    clock.uninstall();
    delete (document as { hidden?: boolean }).hidden;
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    TestBed.resetTestingModule();
  });

  it('reports support from the bridge', () => {
    expect(createService().supported).toBeTrue();
  });

  it('is unsupported without the shell bridge', async () => {
    const service = createService(false);

    expect(service.supported).toBeFalse();

    service.track();
    await advance();

    expect(service.position()).toBeNull();
  });

  it('samples the position as soon as tracking starts', async () => {
    const service = createService();

    service.track();
    await drain();

    expect(service.position()).toEqual({ x: 1920, y: 540 });
  });

  it('follows the cursor across samples', async () => {
    const service = createService();
    service.track();
    await drain();

    getCursorPosition.and.resolveTo({ x: -300, y: 12 });
    await advance();

    expect(service.position()).toEqual({ x: -300, y: 12 });
  });

  it('keeps sampling until the last tracker released', async () => {
    const service = createService();
    const first = service.track();
    const second = service.track();
    await drain();

    first();
    getCursorPosition.calls.reset();
    await advance();

    expect(getCursorPosition).toHaveBeenCalled();

    second();
    getCursorPosition.calls.reset();
    await advance();

    expect(getCursorPosition).not.toHaveBeenCalled();
    expect(service.position()).toBeNull();
  });

  it('ignores a tracker released twice', async () => {
    const service = createService();
    const release = service.track();
    service.track();
    await drain();

    release();
    release();
    getCursorPosition.calls.reset();
    await advance();

    expect(getCursorPosition).toHaveBeenCalled();
  });

  it('does not queue a second sample behind a slow one', async () => {
    let resolve: (position: ShellCursorPosition) => void = () => undefined;
    getCursorPosition.and.returnValue(new Promise(done => (resolve = done)));
    const service = createService();

    service.track();
    await advance(3);

    expect(getCursorPosition).toHaveBeenCalledTimes(1);

    resolve({ x: 5, y: 6 });
    await drain();

    expect(service.position()).toEqual({ x: 5, y: 6 });
  });

  it('skips the round trip while the document is hidden', async () => {
    const service = createService();
    service.track();
    await drain();
    getCursorPosition.calls.reset();
    hidden = true;

    await advance();

    expect(getCursorPosition).not.toHaveBeenCalled();

    hidden = false;
    await advance();

    expect(getCursorPosition).toHaveBeenCalled();
    expect(service.position()).toEqual({ x: 1920, y: 540 });
  });

  it('clears the position when the shell cannot locate the cursor', async () => {
    const service = createService();
    service.track();
    await drain();

    getCursorPosition.and.resolveTo(null);
    await advance();

    expect(service.position()).toBeNull();
  });

  it('stops polling when the bridge call itself fails', async () => {
    getCursorPosition.and.rejectWith(new Error('no ipc grant'));
    const service = createService();

    service.track();
    await drain();

    expect(getCursorPosition).toHaveBeenCalledTimes(1);
    expect(service.position()).toBeNull();

    await advance(3);

    expect(getCursorPosition).toHaveBeenCalledTimes(1);
  });
});

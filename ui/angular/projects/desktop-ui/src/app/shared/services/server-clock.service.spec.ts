import { computed, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ApiService, ConnectionState } from '../transport';
import { ServerClockService } from './server-clock.service';

describe('ServerClockService', () => {
  let connectionState: ReturnType<typeof signal<ConnectionState>>;
  let getServerTime: jasmine.Spy<() => Promise<{ utcMs: number }>>;

  function configure(): void {
    connectionState = signal<ConnectionState>('disconnected');
    getServerTime = jasmine.createSpy('getServerTime');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: { connectionStateSignal: connectionState, getServerTime },
        },
      ],
    });
  }

  beforeEach(() => configure());

  it('reports the device clock until the first sync lands', () => {
    const clock = TestBed.inject(ServerClockService);

    expect(clock.offset()).toBe(0);
    expect(clock.now()).toBeCloseTo(Date.now(), -2);
  });

  it('credits half the round trip to the host timestamp', async () => {
    const clock = TestBed.inject(ServerClockService);
    getServerTime.and.callFake(() => Promise.resolve({ utcMs: Date.now() + 60_000 }));

    await clock.sync();

    expect(clock.offset()).toBeCloseTo(60_000, -2);
    expect(clock.now()).toBeCloseTo(Date.now() + 60_000, -2);
  });

  it('syncs when the transport connects', async () => {
    const clock = TestBed.inject(ServerClockService);
    getServerTime.and.callFake(() => Promise.resolve({ utcMs: Date.now() - 5000 }));

    connectionState.set('connected');
    await TestBed.inject(ServerClockService).sync();

    expect(getServerTime).toHaveBeenCalled();
    expect(clock.offset()).toBeCloseTo(-5000, -2);
  });

  it('keeps the previous offset when the host cannot be reached', async () => {
    const clock = TestBed.inject(ServerClockService);
    getServerTime.and.callFake(() => Promise.resolve({ utcMs: Date.now() + 30_000 }));
    await clock.sync();

    getServerTime.and.returnValue(Promise.reject(new Error('offline')));
    await clock.sync();

    expect(clock.offset()).toBeCloseTo(30_000, -2);
  });

  it('invalidates anchors taken while the document was hidden', () => {
    const clock = TestBed.inject(ServerClockService);
    getServerTime.and.callFake(() => Promise.resolve({ utcMs: Date.now() }));
    let reads = 0;
    const anchor = computed(() => {
      reads += 1;
      return clock.now();
    });
    anchor();

    document.dispatchEvent(new Event('visibilitychange'));

    anchor();
    expect(reads).toBe(2);
  });

  it('runs a single sync at a time', async () => {
    const clock = TestBed.inject(ServerClockService);
    let resolveTime: (value: { utcMs: number }) => void = () => undefined;
    getServerTime.and.returnValue(new Promise(resolve => (resolveTime = resolve)));

    const first = clock.sync();
    const second = clock.sync();
    resolveTime({ utcMs: Date.now() });
    await Promise.all([first, second]);

    expect(getServerTime).toHaveBeenCalledTimes(1);
  });
});

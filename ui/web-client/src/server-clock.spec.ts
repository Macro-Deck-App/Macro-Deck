import { HttpClient } from '@macro-deck/runtime';
import { ServerClock } from './server-clock';

describe('server clock', () => {
  let requests: Array<(utcMs: number) => void>;
  let failures: Array<() => void>;
  let http: HttpClient;

  beforeEach(() => {
    requests = [];
    failures = [];
    http = {
      get: () => new Promise((resolve, reject) => {
        requests.push(utcMs => resolve({ utcMs } as never));
        failures.push(() => reject(new Error('offline')));
      }),
    } as unknown as HttpClient;
  });

  // A microtask drain rather than a timer: every promise here resolves immediately, and a spec that
  // waits on a real timeout stops working the moment another suite installs a fake clock.
  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) await Promise.resolve();
  };

  it('reads the device clock until the first sync lands', () => {
    const clock = new ServerClock(http);

    // One client stays internally consistent on its own clock; it is only agreement between clients
    // that needs the host.
    expect(Math.abs(clock.now() - Date.now())).toBeLessThan(50);
    expect(clock.offset()).toBe(0);
  });

  it('credits half the round trip to the response rather than all of it', async () => {
    const clock = new ServerClock(http);
    const before = Date.now();
    void clock.sync();

    // A host one hour ahead, answering instantly: the offset must come out as the hour, not the hour
    // plus however long the request happened to take.
    requests[0](before + 3600_000);
    await settle();

    expect(clock.offset()).toBeGreaterThan(3600_000 - 100);
    expect(clock.offset()).toBeLessThan(3600_000 + 100);
    expect(clock.now()).toBeGreaterThan(Date.now() + 3600_000 - 100);
  });

  it('keeps the offset it had when a sync fails', async () => {
    const clock = new ServerClock(http);
    void clock.sync();
    requests[0](Date.now() + 5000);
    await settle();
    const settled = clock.offset();

    void clock.sync();
    failures[1]();
    await settle();

    // Resetting to the device clock would jump every border animation on a dropped request.
    expect(clock.offset()).toBe(settled);
  });

  it('measures once while a measurement is already in flight', async () => {
    const clock = new ServerClock(http);

    void clock.sync();
    void clock.sync();
    void clock.sync();

    expect(requests.length).toBe(1);
  });
});

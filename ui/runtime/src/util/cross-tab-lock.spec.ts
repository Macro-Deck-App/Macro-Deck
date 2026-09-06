import { CrossTabLockEnvironment, CrossTabLockStorage, runExclusively } from './cross-tab-lock';

function fakeStorage(initial: Record<string, string> = {}): CrossTabLockStorage & { values: Record<string, string> } {
  const values = { ...initial };
  return {
    values,
    getItem: (key: string) => values[key] ?? null,
    setItem: (key: string, value: string) => {
      values[key] = value;
    },
    removeItem: (key: string) => {
      delete values[key];
    },
  };
}

function environment(overrides: Partial<CrossTabLockEnvironment> = {}): CrossTabLockEnvironment {
  let clock = 1_000_000;
  return {
    locks: null,
    storage: fakeStorage(),
    now: () => clock,
    delay: async (ms: number) => {
      clock += ms;
    },
    ...overrides,
  };
}

describe('runExclusively', () => {
  it('runs the work through the Web Locks mutex when the origin has one', async () => {
    // Web Locks is a real mutex and needs no TTL guessing, so it must be preferred wherever it
    // exists - the storage scheme below can only ever narrow the race, not close it.
    const calls: string[] = [];
    const locks = {
      request: async <T>(name: string, callback: () => Promise<T>): Promise<T> => {
        calls.push(name);
        return callback();
      },
    };

    const result = await runExclusively('lock-a', async () => 'done', environment({ locks }));

    expect(result).toBe('done');
    expect(calls).toEqual(['lock-a']);
  });

  it('waits for a lock another tab holds instead of running alongside it', async () => {
    const storage = fakeStorage();
    let clock = 1_000_000;
    const env = environment({
      storage,
      now: () => clock,
      delay: async (ms: number) => {
        clock += ms;
        storage.removeItem('lock-a');
      },
    });
    storage.setItem('lock-a', String(clock));
    let ranAt: number | null = null;

    await runExclusively('lock-a', async () => {
      ranAt = clock;
    }, env);

    expect(ranAt).not.toBeNull();
    expect(ranAt!).toBeGreaterThan(1_000_000);
  });

  it('takes over a lock whose holder died', async () => {
    const storage = fakeStorage();
    let clock = 1_000_000;
    storage.setItem('lock-a', String(clock - 60_000));
    let ran = false;

    await runExclusively('lock-a', async () => {
      ran = true;
    }, environment({ storage, now: () => clock, delay: async ms => { clock += ms; } }));

    expect(ran).toBeTrue();
  });

  it('releases the lock even when the work throws', async () => {
    const storage = fakeStorage();

    await expectAsync(
      runExclusively('lock-a', () => Promise.reject(new Error('boom')), environment({ storage }))
    ).toBeRejected();

    expect(storage.getItem('lock-a')).toBeNull();
  });

  it('still runs the work when there is no storage to lock with', async () => {
    let ran = false;

    await runExclusively('lock-a', async () => {
      ran = true;
    }, environment({ storage: null }));

    expect(ran).toBeTrue();
  });
});

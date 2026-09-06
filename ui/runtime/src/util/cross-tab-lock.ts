export interface WebLockManager {
  request<T>(name: string, callback: () => Promise<T>): Promise<T>;
}

export type CrossTabLockStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

export interface CrossTabLockEnvironment {
  readonly locks: WebLockManager | null;
  readonly storage: CrossTabLockStorage | null;
  readonly now: () => number;
  readonly delay: (ms: number) => Promise<void>;
}

const LOCK_TTL_MS = 5000;
const LOCK_RENEW_MS = 2000;
const LOCK_WAIT_MS = 5000;
const LOCK_POLL_MS = 300;

export function browserLockEnvironment(): CrossTabLockEnvironment {
  return {
    locks: (navigator as unknown as { locks?: WebLockManager }).locks ?? null,
    storage: readLocalStorage(),
    now: () => Date.now(),
    delay: ms => new Promise(resolve => setTimeout(resolve, ms)),
  };
}

function readLocalStorage(): CrossTabLockStorage | null {
  try {
    return localStorage;
  } catch {
    return null;
  }
}

export async function runExclusively<T>(
  name: string,
  work: () => Promise<T>,
  environment: CrossTabLockEnvironment = browserLockEnvironment()
): Promise<T> {
  if (environment.locks) {
    return environment.locks.request(name, work);
  }

  const storage = environment.storage;
  if (!storage) {
    return work();
  }

  const waitUntil = environment.now() + LOCK_WAIT_MS;
  while (isHeldByAnother(storage, name, environment.now) && environment.now() < waitUntil) {
    await environment.delay(LOCK_POLL_MS);
  }
  write(storage, name, environment.now());

  const renewal = setInterval(() => write(storage, name, environment.now()), LOCK_RENEW_MS);
  try {
    return await work();
  } finally {
    clearInterval(renewal);
    remove(storage, name);
  }
}

function isHeldByAnother(storage: CrossTabLockStorage, name: string, now: () => number): boolean {
  try {
    const raw = storage.getItem(name);
    if (raw === null) {
      return false;
    }
    const heldAt = Number(raw);
    return Number.isFinite(heldAt) && now() - heldAt < LOCK_TTL_MS;
  } catch {
    return false;
  }
}

function write(storage: CrossTabLockStorage, name: string, at: number): void {
  try {
    storage.setItem(name, String(at));
  } catch {
  }
}

function remove(storage: CrossTabLockStorage, name: string): void {
  try {
    storage.removeItem(name);
  } catch {
  }
}

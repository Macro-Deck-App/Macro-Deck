export interface WebLockManager {
  request<T>(name: string, options: { signal?: AbortSignal }, callback: () => Promise<T>): Promise<T>;
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
const WEB_LOCK_WAIT_MS = 45000;

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
    return runWithWebLock(environment.locks, name, work, environment);
  }

  return runWithStorageLock(name, work, environment);
}

// Past the wait the work runs without exclusion, so a holder that never lets go cannot block it.
async function runWithWebLock<T>(
  locks: WebLockManager,
  name: string,
  work: () => Promise<T>,
  environment: CrossTabLockEnvironment
): Promise<T> {
  let entered = false;
  const controller = typeof AbortController === 'undefined' ? null : new AbortController();
  const timer = controller === null ? null : setTimeout(() => controller.abort(), WEB_LOCK_WAIT_MS);
  try {
    return await locks.request(name, controller === null ? {} : { signal: controller.signal }, () => {
      entered = true;
      if (timer !== null) clearTimeout(timer);
      return work();
    });
  } catch (error) {
    if (entered) throw error;
    return runWithStorageLock(name, work, environment);
  } finally {
    if (timer !== null) clearTimeout(timer);
  }
}

async function runWithStorageLock<T>(
  name: string,
  work: () => Promise<T>,
  environment: CrossTabLockEnvironment
): Promise<T> {
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

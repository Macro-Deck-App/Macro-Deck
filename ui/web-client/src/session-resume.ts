const COALESCE_WINDOW_MS = 1000;

const RESUME_TIMEOUT_MS = 3000;

export interface SessionResumeAuth {
  resume(): Promise<void>;
  isAuthenticated(): boolean;
}

export interface SessionResumeConnection {
  reconnectNow(): void;
}

export interface ResumeTimer {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

const REAL_TIMER: ResumeTimer = {
  set: (callback: () => void, delayMs: number) => setTimeout(callback, delayMs),
  clear: (handle: unknown) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface SessionResumeOptions {
  doc?: Document;
  timer?: ResumeTimer;
}

export interface SessionResumeHandler {
  onVisible(): void;
  cancel(): void;
}

function withTimeout(promise: Promise<unknown>, ms: number, timer: ResumeTimer): Promise<void> {
  return new Promise<void>(resolve => {
    let settled = false;
    const settle = (): void => {
      if (settled) return;
      settled = true;
      resolve();
    };
    const handle = timer.set(settle, ms);
    const done = (): void => {
      timer.clear(handle);
      settle();
    };
    promise.then(done, done);
  });
}

export function createSessionResumeHandler(
  auth: SessionResumeAuth,
  connection: SessionResumeConnection,
  options?: SessionResumeOptions,
): SessionResumeHandler {
  const settings = options === undefined ? {} : options;
  const doc = settings.doc === undefined ? document : settings.doc;
  const timer = settings.timer === undefined ? REAL_TIMER : settings.timer;

  let windowHandle: unknown = null;
  let trailingPending = false;

  const run = (): void => {
    if (doc.hidden || !auth.isAuthenticated()) return;
    const reconnect = (): void => connection.reconnectNow();
    void withTimeout(auth.resume(), RESUME_TIMEOUT_MS, timer).then(reconnect, reconnect);
  };

  return {
    onVisible: () => {
      if (doc.hidden) return;

      if (windowHandle === null) {
        run();
        windowHandle = timer.set(() => {
          windowHandle = null;
          if (!trailingPending) return;
          trailingPending = false;
          run();
        }, COALESCE_WINDOW_MS);
        return;
      }

      trailingPending = true;
    },
    cancel: () => {
      if (windowHandle !== null) {
        timer.clear(windowHandle);
        windowHandle = null;
      }
      trailingPending = false;
    },
  };
}

export function startSessionResume(
  auth: SessionResumeAuth,
  connection: SessionResumeConnection,
  options?: SessionResumeOptions,
): () => void {
  const settings = options === undefined ? {} : options;
  const doc = settings.doc === undefined ? document : settings.doc;
  const handler = createSessionResumeHandler(auth, connection, settings);

  const listener = (): void => {
    if (!doc.hidden) handler.onVisible();
  };
  doc.addEventListener('visibilitychange', listener);

  return () => {
    doc.removeEventListener('visibilitychange', listener);
    handler.cancel();
  };
}

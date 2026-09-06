import {
  createSessionResumeHandler,
  type ResumeTimer,
  type SessionResumeAuth,
  type SessionResumeConnection,
} from './session-resume';

function manualTimer(): ResumeTimer & { fire(): void; pending(): number } {
  let handles: Array<{ id: number; callback: () => void }> = [];
  let next = 1;
  return {
    set: (callback: () => void) => {
      const id = next++;
      handles.push({ id: id, callback: callback });
      return id;
    },
    clear: (handle: unknown) => {
      handles = handles.filter(entry => entry.id !== handle);
    },
    fire: () => {
      const due = handles;
      handles = [];
      for (let index = 0; index < due.length; index++) due[index].callback();
    },
    pending: () => handles.length,
  };
}

function hidden(value: boolean): Document {
  return { hidden: value } as unknown as Document;
}

function auth(overrides?: Partial<SessionResumeAuth>): SessionResumeAuth & { resume: jasmine.Spy } {
  const base = {
    resume: jasmine.createSpy('resume').and.resolveTo(undefined),
    isAuthenticated: () => true,
  };
  return { ...base, ...(overrides === undefined ? {} : overrides) } as never;
}

function connection(): SessionResumeConnection & { reconnectNow: jasmine.Spy } {
  return { reconnectNow: jasmine.createSpy('reconnectNow') };
}

describe('createSessionResumeHandler', () => {
  it('refreshes and reconnects on the first transition, without waiting', async () => {
    const session = auth();
    const api = connection();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(false), timer: manualTimer() });

    handler.onVisible();
    await Promise.resolve();
    await Promise.resolve();

    expect(session.resume).toHaveBeenCalledTimes(1);
    expect(api.reconnectNow).toHaveBeenCalledTimes(1);
  });

  it('reconnects even when the refresh fails', async () => {
    const session = auth({ resume: jasmine.createSpy('resume').and.rejectWith(new Error('offline')) });
    const api = connection();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(false), timer: manualTimer() });

    handler.onVisible();
    await Promise.resolve();
    await Promise.resolve();

    expect(api.reconnectNow).toHaveBeenCalledTimes(1);
  });

  it('reconnects once the wait elapses even while the refresh hangs', async () => {
    const session = auth({ resume: () => new Promise<void>(() => undefined) });
    const api = connection();
    const timer = manualTimer();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(false), timer: timer });

    handler.onVisible();
    timer.fire();
    await Promise.resolve();
    await Promise.resolve();

    expect(api.reconnectNow).toHaveBeenCalledTimes(1);
  });

  it('does nothing while the document is hidden', () => {
    const session = auth();
    const api = connection();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(true), timer: manualTimer() });

    handler.onVisible();

    expect(session.resume).not.toHaveBeenCalled();
    expect(api.reconnectNow).not.toHaveBeenCalled();
  });

  it('does nothing while nobody is signed in', () => {
    const session = auth({ isAuthenticated: () => false });
    const api = connection();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(false), timer: manualTimer() });

    handler.onVisible();

    expect(session.resume).not.toHaveBeenCalled();
    expect(api.reconnectNow).not.toHaveBeenCalled();
  });

  it('merges the pair of events an unlock fires into one trailing run', async () => {
    const session = auth();
    const api = connection();
    const timer = manualTimer();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(false), timer: timer });

    handler.onVisible();
    handler.onVisible();
    handler.onVisible();
    await Promise.resolve();
    expect(session.resume).toHaveBeenCalledTimes(1);

    timer.fire();
    await Promise.resolve();

    expect(session.resume).toHaveBeenCalledTimes(2);
  });

  it('drops a coalesced run that a teardown cancelled', async () => {
    const session = auth();
    const api = connection();
    const timer = manualTimer();
    const handler = createSessionResumeHandler(session, api, { doc: hidden(false), timer: timer });

    handler.onVisible();
    handler.onVisible();
    handler.cancel();
    timer.fire();
    await Promise.resolve();

    expect(session.resume).toHaveBeenCalledTimes(1);
  });
});

import { AppUpdate, type UpdateSource } from './app-update';

class FakeDocument {
  hidden = false;
  private readonly listeners: Array<() => void> = [];

  addEventListener(_type: string, listener: () => void): void {
    this.listeners.push(listener);
  }

  removeEventListener(_type: string, listener: () => void): void {
    const index = this.listeners.indexOf(listener);
    if (index >= 0) this.listeners.splice(index, 1);
  }

  setHidden(hidden: boolean): void {
    this.hidden = hidden;
    for (const listener of this.listeners.slice()) listener();
  }
}

function memoryStorage(seed?: Record<string, string>): Storage {
  const values: Record<string, string> = {};
  if (seed) for (const key of Object.keys(seed)) values[key] = seed[key];
  return {
    get length(): number { return Object.keys(values).length; },
    clear: () => { for (const key of Object.keys(values)) delete values[key]; },
    getItem: (key: string) => (key in values ? values[key] : null),
    key: (index: number) => { const found = Object.keys(values)[index]; return found === undefined ? null : found; },
    removeItem: (key: string) => { delete values[key]; },
    setItem: (key: string, value: string) => { values[key] = value; },
  } as Storage;
}

class FakeSource implements UpdateSource {
  enabled = true;
  checkResult: Promise<boolean> = Promise.resolve(false);
  activateResult: Promise<void> = Promise.resolve();
  checks = 0;
  activations = 0;

  private versionListener: ((version: string) => void) | null = null;
  private unrecoverableListener: (() => void) | null = null;

  onVersionReady(listener: (version: string) => void): void { this.versionListener = listener; }
  onUnrecoverable(listener: () => void): void { this.unrecoverableListener = listener; }

  checkForUpdate(): Promise<boolean> {
    this.checks++;
    return this.checkResult;
  }

  activateUpdate(): Promise<void> {
    this.activations++;
    return this.activateResult;
  }

  emitVersionReady(version: string): void { if (this.versionListener) this.versionListener(version); }
  emitUnrecoverable(): void { if (this.unrecoverableListener) this.unrecoverableListener(); }
}

function settled(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, 0));
}

interface Harness {
  update: AppUpdate;
  source: FakeSource;
  doc: FakeDocument;
  reloads: () => number;
  storage: Storage;
}

function harness(options?: { storage?: Storage }): Harness {
  const source = new FakeSource();
  const doc = new FakeDocument();
  const storage = options && options.storage ? options.storage : memoryStorage();
  let reloads = 0;
  const update = new AppUpdate(source, {
    document: doc as unknown as Document,
    storage,
    reload: () => { reloads++; },
  });
  return { update, source, doc, reloads: () => reloads, storage };
}

const ATTEMPTS_KEY = 'md.update.reloadAttempts';

describe('AppUpdate', () => {
  it('reports unsupported and stays inert without a registered worker', async () => {
    const source = new FakeSource();
    source.enabled = false;
    const update = new AppUpdate(source, { document: new FakeDocument() as unknown as Document });

    await update.check();

    expect(update.phase()).toBe('unsupported');
    expect(source.checks).toBe(0);
  });

  it('reports an explicit check that finds nothing as up to date', async () => {
    const { update } = harness();

    const pending = update.check();
    expect(update.phase()).toBe('checking');
    await pending;

    expect(update.phase()).toBe('upToDate');
  });

  it('reports an explicit check that could not run as failed', async () => {
    const { update, source } = harness();
    source.checkResult = Promise.reject(new Error('offline'));

    await update.check();

    expect(update.phase()).toBe('checkFailed');
  });

  it('keeps a background check silent about its own failure', async () => {
    const { update, source } = harness();
    source.checkResult = Promise.reject(new Error('offline'));

    await update.backgroundCheck();

    expect(update.phase()).toBe('idle');
  });

  it('never shows a background check as checking', async () => {
    const { update } = harness();

    const pending = update.backgroundCheck();
    expect(update.phase()).toBe('idle');
    await pending;

    expect(update.phase()).toBe('upToDate');
  });

  it('does not let a background check downgrade an available update', async () => {
    const { update, source } = harness();
    source.emitVersionReady('version-2');

    await update.backgroundCheck();

    expect(update.phase()).toBe('available');
  });

  it('checks in the background when the tab becomes visible again', async () => {
    const { source, doc } = harness();

    doc.setHidden(true);
    doc.setHidden(false);
    await settled();

    expect(source.checks).toBe(1);
  });

  it('waits for the document to go hidden before applying a ready update', async () => {
    const { update, source, doc, reloads } = harness();

    source.emitVersionReady('version-2');
    await settled();

    expect(update.phase()).toBe('available');
    expect(source.activations).toBe(0);

    doc.setHidden(true);
    await settled();

    expect(source.activations).toBe(1);
    expect(update.phase()).toBe('reloading');
    expect(reloads()).toBe(1);
  });

  it('applies a ready update at once when the document is already hidden', async () => {
    const { source, doc, reloads } = harness();
    doc.hidden = true;

    source.emitVersionReady('version-2');
    await settled();

    expect(source.activations).toBe(1);
    expect(reloads()).toBe(1);
  });

  it('applies a ready update on request without waiting for the document to hide', async () => {
    const { update, source, reloads } = harness();
    source.emitVersionReady('version-2');

    await update.activateNow();

    expect(source.activations).toBe(1);
    expect(reloads()).toBe(1);
    expect(update.phase()).toBe('reloading');
  });

  it('still reloads after a failed activation, but reports it', async () => {
    const { update, source, reloads } = harness();
    source.activateResult = Promise.reject(new Error('no worker'));
    source.emitVersionReady('version-2');

    await update.activateNow();

    expect(update.phase()).toBe('applyFailed');
    expect(reloads()).toBe(1);
  });

  it('stops reloading once a version has used up its attempts', async () => {
    const storage = memoryStorage({
      [ATTEMPTS_KEY]: JSON.stringify({ target: 'version-2', attempts: 3 }),
    });
    const { update, source, reloads } = harness({ storage });
    source.emitVersionReady('version-2');

    await update.activateNow();

    expect(reloads()).toBe(0);
    expect(update.phase()).toBe('applyFailed');
  });

  it('gives a newly shipped version its own attempts', async () => {
    const storage = memoryStorage({
      [ATTEMPTS_KEY]: JSON.stringify({ target: 'version-2', attempts: 3 }),
    });
    const { update, source, reloads } = harness({ storage });
    source.emitVersionReady('version-3');

    await update.activateNow();

    expect(reloads()).toBe(1);
  });

  it('counts each attempt against the version it was made for', async () => {
    const { update, source, storage } = harness();
    source.emitVersionReady('version-2');

    await update.activateNow();

    expect(JSON.parse(storage.getItem(ATTEMPTS_KEY) as string))
      .toEqual({ target: 'version-2', attempts: 1 });
  });

  it('reloads once for an unrecoverable worker state, and only once', async () => {
    const { update, source, reloads } = harness();

    source.emitUnrecoverable();
    source.emitUnrecoverable();
    await settled();

    expect(update.phase()).toBe('reloading');
    expect(reloads()).toBe(1);
  });
});

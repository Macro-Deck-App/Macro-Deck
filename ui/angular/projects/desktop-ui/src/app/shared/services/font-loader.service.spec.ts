import { ErrorHandler, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ApiService } from '../transport';
import {
  FONT_FACE_BACKEND,
  FontFaceBackend,
  FontFaceHandle,
  FontLoaderService,
  internalFontFamily,
} from './font-loader.service';

const FACE_ID = 'helvetica-neue-700-5-upright';

interface RecordedCreate {
  family: string;
  source: string;
  descriptors: FontFaceDescriptors;
}

class RecordingFontFaceBackend implements FontFaceBackend {
  readonly createCalls: RecordedCreate[] = [];
  readonly addCalls: FontFaceHandle[] = [];
  private readonly pending: Array<{
    handle: FontFaceHandle;
    resolve: (handle: FontFaceHandle) => void;
    reject: (err: unknown) => void;
  }> = [];

  create(family: string, source: string, descriptors: FontFaceDescriptors): FontFaceHandle {
    this.createCalls.push({ family, source, descriptors });
    let resolve!: (handle: FontFaceHandle) => void;
    let reject!: (err: unknown) => void;
    const promise = new Promise<FontFaceHandle>((res, rej) => { resolve = res; reject = rej; });
    const handle: FontFaceHandle = { load: () => promise };
    this.pending.push({ handle, resolve, reject });
    return handle;
  }

  add(face: FontFaceHandle): void {
    this.addCalls.push(face);
  }

  get lastLoadPromise(): Promise<FontFaceHandle> {
    return this.pending[this.pending.length - 1].handle.load();
  }

  resolveLast(): void {
    const entry = this.pending.pop();
    entry?.resolve(entry.handle);
  }

  rejectLast(err: unknown): void {
    const entry = this.pending.pop();
    entry?.reject(err);
  }
}

function configure(backend: FontFaceBackend, errorHandler?: ErrorHandler): {
  loader: FontLoaderService;
  api: jasmine.SpyObj<ApiService>;
} {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getFontFileUrl']);
  api.getFontFileUrl.and.callFake(faceId => `http://host/api/system/fonts/${faceId}/file`);

  const providers: unknown[] = [
    provideZonelessChangeDetection(),
    { provide: ApiService, useValue: api },
    { provide: FONT_FACE_BACKEND, useValue: backend },
  ];
  if (errorHandler) {
    providers.push({ provide: ErrorHandler, useValue: errorHandler });
  }
  TestBed.configureTestingModule({ providers });
  return { loader: TestBed.inject(FontLoaderService), api };
}

async function flush(turns = 3): Promise<void> {
  for (let i = 0; i < turns; i++) {
    await Promise.resolve();
  }
}

describe('FontLoaderService no face configured', () => {
  it('reports ready immediately and never touches the backend', () => {
    const backend = new RecordingFontFaceBackend();
    const { loader, api } = configure(backend);

    const status = loader.ensureFace(undefined);

    expect(status()).toBe('ready');
    expect(backend.createCalls.length).toBe(0);
    expect(api.getFontFileUrl).not.toHaveBeenCalled();
  });
});

describe('FontLoaderService registration (issue #457 finding 6.1, REGRESSION)', () => {
  it('fetches and registers under the internal family even when a local check would say the family is already installed', async () => {
    // Simulates "the family is installed locally" - document.fonts.check() returning true for
    // every spec. A half-fix that still consults this before deciding whether to fetch would
    // short-circuit here and never download the host's face at all.
    spyOn(document.fonts, 'check').and.returnValue(true);

    const backend = new RecordingFontFaceBackend();
    const { loader, api } = configure(backend);

    const status = loader.ensureFace(FACE_ID);

    expect(api.getFontFileUrl).toHaveBeenCalledTimes(1);
    expect(api.getFontFileUrl).toHaveBeenCalledWith(FACE_ID);
    expect(backend.createCalls.length).toBe(1);
    const created = backend.createCalls[0];
    // Asserted both ways: a half-fix that renames the family but keeps checking document.fonts, or
    // one that drops the check but keeps registering under the bare family, both fail one of these.
    expect(created.family).toMatch(/^MacroDeckFont_/);
    expect(created.family).not.toBe(FACE_ID);
    expect(created.family).toBe(internalFontFamily(FACE_ID));
    expect(created.descriptors.weight).toBe('700');
    expect(created.descriptors.style).toBe('normal');

    backend.resolveLast();
    await flush();

    expect(backend.addCalls.length).toBe(1);
    expect(status()).toBe('ready');
  });
});

describe('FontLoaderService dedup and readiness ordering (issue #457 finding 7.2)', () => {
  it('shares one fetch across concurrent callers, both settling only after the face was added', async () => {
    const backend = new RecordingFontFaceBackend();
    const { loader, api } = configure(backend);

    const status1 = loader.ensureFace(FACE_ID);
    const status2 = loader.ensureFace(FACE_ID);

    // Deduplication by "already attempted -> report ready immediately" would make this true from
    // the start, which is exactly the regression this pins against.
    expect(status1()).toBe('loading');
    expect(status2()).toBe('loading');
    expect(api.getFontFileUrl).toHaveBeenCalledTimes(1);
    expect(backend.createCalls.length).toBe(1);

    const order: string[] = [];
    backend.lastLoadPromise.then(() => order.push('load settled'));

    backend.resolveLast();
    await flush();

    expect(order).toEqual(['load settled']);
    expect(backend.addCalls.length).toBe(1);
    expect(status1()).toBe('ready');
    expect(status2()).toBe('ready');
    order.push('caller1 observed ready', 'caller2 observed ready');
    expect(order).toEqual(['load settled', 'caller1 observed ready', 'caller2 observed ready']);
  });
});

describe('FontLoaderService failure handling (issue #457 finding 8.1, REGRESSION)', () => {
  it('reports a rejected load through ErrorHandler, settles to failed, and never throws unhandled', async () => {
    const backend = new RecordingFontFaceBackend();
    const errorHandler = jasmine.createSpyObj<ErrorHandler>('ErrorHandler', ['handleError']);
    const { loader } = configure(backend, errorHandler);

    const status = loader.ensureFace(FACE_ID);
    backend.rejectLast(new Error('network down'));
    await flush();

    expect(status()).toBe('failed');
    expect(errorHandler.handleError).toHaveBeenCalledTimes(1);
    const reported = errorHandler.handleError.calls.mostRecent().args[0] as Error;
    expect(reported.message).toContain(FACE_ID);
    expect(reported.message).toContain('network down');
  });
});

describe('FontLoaderService failure recovery (issue #457 finding 8.2)', () => {
  it('retries the fetch on a later request for the same face after a failure', async () => {
    const backend = new RecordingFontFaceBackend();
    const { loader, api } = configure(backend);

    loader.ensureFace(FACE_ID);
    backend.rejectLast(new Error('network down'));
    await flush();

    const status2 = loader.ensureFace(FACE_ID);

    expect(status2()).toBe('loading');
    expect(api.getFontFileUrl).toHaveBeenCalledTimes(2);

    backend.resolveLast();
    await flush();

    expect(status2()).toBe('ready');
  });
});

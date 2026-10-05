import { ErrorHandler, Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { ApiService } from '../transport';

export type FontFaceLoadStatus = 'loading' | 'ready' | 'failed';

export interface FontFaceHandle {
  load(): Promise<FontFaceHandle>;
}

export interface FontFaceBackend {
  create(family: string, source: string, descriptors: FontFaceDescriptors): FontFaceHandle;
  add(face: FontFaceHandle): void;
  delete(face: FontFaceHandle): void;
}

function createBrowserFontFaceBackend(): FontFaceBackend | null {
  if (typeof document === 'undefined' || typeof FontFace === 'undefined' || !('fonts' in document)) {
    return null;
  }
  return {
    create: (family, source, descriptors) => new FontFace(family, source, descriptors),
    add: face => document.fonts.add(face as unknown as FontFace),
    delete: face => document.fonts.delete(face as unknown as FontFace),
  };
}

export const FONT_FACE_BACKEND = new InjectionToken<FontFaceBackend | null>('FONT_FACE_BACKEND', {
  providedIn: 'root',
  factory: () => createBrowserFontFaceBackend(),
});

export function internalFontFamily(faceId: string): string {
  return `MacroDeckFont_${faceId}`;
}

function descriptorsFor(faceId: string): FontFaceDescriptors {
  // The trailing `-2`, `-3`... is the catalog's collision suffix for faces that share all four id
  // components, so it has to be tolerated here or those faces get no descriptors at all.
  const match = /-(\d+)-\d+-(upright|italic|oblique)(?:-\d+)?$/.exec(faceId);
  if (!match) {
    return {};
  }
  const [, weight, slant] = match;
  return { weight, style: slant === 'upright' ? 'normal' : slant };
}

const READY_STATUS: Signal<FontFaceLoadStatus> = signal<FontFaceLoadStatus>('ready').asReadonly();

const RETRY_AFTER_MS = 30_000;

interface CachedFace {
  status: WritableSignal<FontFaceLoadStatus>;
  handle?: FontFaceHandle;
  failedAt?: number;
}

// Faces are always fetched from the host, desktop app included (issue #457): the desktop cost is one
// loopback request against an immutable response, and branching on desktop-vs-remote would
// reintroduce the substitution bug on exactly the clients that suffer from it.
@Injectable({ providedIn: 'root' })
export class FontLoaderService {
  private readonly api = inject(ApiService);
  private readonly backend = inject(FONT_FACE_BACKEND, { optional: true }) ?? null;
  private readonly errorHandler = inject(ErrorHandler);

  private readonly cache = new Map<string, CachedFace>();

  ensureFace(faceId: string | undefined): Signal<FontFaceLoadStatus> {
    if (!faceId) {
      return READY_STATUS;
    }

    const cached = this.cache.get(faceId);
    if (cached) {
      // A failed face stays failed and is retried in the background at most every RETRY_AFTER_MS, so
      // text in a missing face neither refetches on each paint nor blinks while a retry runs.
      if (cached.failedAt !== undefined && Date.now() - cached.failedAt >= RETRY_AFTER_MS) {
        cached.failedAt = Date.now();
        void this.load(faceId, cached);
      }
      return cached.status.asReadonly();
    }

    const entry: CachedFace = { status: signal<FontFaceLoadStatus>('loading') };
    this.cache.set(faceId, entry);
    void this.load(faceId, entry);
    return entry.status.asReadonly();
  }

  evict(faceIds: readonly string[]): void {
    for (const faceId of faceIds) {
      const cached = this.cache.get(faceId);
      if (!cached) {
        continue;
      }
      this.cache.delete(faceId);
      if (cached.handle) {
        this.backend?.delete(cached.handle);
      }
    }
  }

  private async load(faceId: string, entry: CachedFace): Promise<void> {
    const status = entry.status;
    if (!this.backend) {
      entry.failedAt = Date.now();
      status.set('failed');
      return;
    }

    try {
      const family = internalFontFamily(faceId);
      const source = `url(${this.api.getFontFileUrl(faceId)})`;
      const face = this.backend.create(family, source, descriptorsFor(faceId));
      const loaded = await face.load();
      // An eviction while the file was in flight means the face is gone; registering it would revive it.
      if (this.cache.get(faceId) !== entry) {
        return;
      }
      entry.handle = loaded;
      this.backend.add(loaded);
      status.set('ready');
    } catch (err) {
      this.errorHandler.handleError(
        new Error(`Failed to load font face '${faceId}': ${err instanceof Error ? err.message : String(err)}`),
      );
      entry.failedAt = Date.now();
      status.set('failed');
    }
  }
}

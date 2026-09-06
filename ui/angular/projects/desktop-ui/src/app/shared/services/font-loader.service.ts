import { ErrorHandler, Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { ApiService } from '../transport';

export type FontFaceLoadStatus = 'loading' | 'ready' | 'failed';

export interface FontFaceHandle {
  load(): Promise<FontFaceHandle>;
}

export interface FontFaceBackend {
  create(family: string, source: string, descriptors: FontFaceDescriptors): FontFaceHandle;
  add(face: FontFaceHandle): void;
}

function createBrowserFontFaceBackend(): FontFaceBackend | null {
  if (typeof document === 'undefined' || typeof FontFace === 'undefined' || !('fonts' in document)) {
    return null;
  }
  return {
    create: (family, source, descriptors) => new FontFace(family, source, descriptors),
    add: face => document.fonts.add(face as unknown as FontFace),
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

interface CachedFace {
  status: WritableSignal<FontFaceLoadStatus>;
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
      return cached.status.asReadonly();
    }

    const status = signal<FontFaceLoadStatus>('loading');
    this.cache.set(faceId, { status });
    void this.load(faceId, status);
    return status.asReadonly();
  }

  private async load(faceId: string, status: WritableSignal<FontFaceLoadStatus>): Promise<void> {
    if (!this.backend) {
      status.set('failed');
      this.cache.delete(faceId);
      return;
    }

    try {
      const family = internalFontFamily(faceId);
      const source = `url(${this.api.getFontFileUrl(faceId)})`;
      const face = this.backend.create(family, source, descriptorsFor(faceId));
      const loaded = await face.load();
      this.backend.add(loaded);
      status.set('ready');
    } catch (err) {
      this.errorHandler.handleError(
        new Error(`Failed to load font face '${faceId}': ${err instanceof Error ? err.message : String(err)}`),
      );
      this.cache.delete(faceId);
      status.set('failed');
    }
  }
}

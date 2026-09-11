import type { SystemFontFace } from '../protocol/messages/system';
import { supportsCustomProperties } from './custom-properties';

export const UI_FONT_FAMILY = 'MacroDeckUiFont';

export interface UiFontBackend {
  create(family: string, source: string, descriptors: FontFaceDescriptors): unknown;
  add(face: unknown): void;
  remove(face: unknown): void;
  onLoadingDone(listener: (faces: readonly unknown[]) => void): void;
}

export function browserUiFontBackend(): UiFontBackend | null {
  if (typeof document === 'undefined' || typeof FontFace === 'undefined' || !('fonts' in document)) {
    return null;
  }
  const fonts = document.fonts as unknown as {
    add(face: unknown): void;
    delete(face: unknown): void;
    addEventListener?(type: string, listener: (event: { fontfaces?: readonly unknown[] }) => void): void;
  };
  return {
    create: (family, source, descriptors) => new FontFace(family, source, descriptors),
    add: face => fonts.add(face),
    remove: face => fonts.delete(face),
    onLoadingDone: listener => fonts.addEventListener?.('loadingdone', event => listener(event.fontfaces ?? [])),
  };
}

function quoteFamily(family: string): string {
  return `"${family.replace(/["\\]/g, '\\$&')}"`;
}

export class UiFont {
  private faces: unknown[] = [];
  private key = '';
  private changes = 0;
  private readonly listeners: Array<() => void> = [];

  constructor(
    private readonly root: HTMLElement = document.documentElement,
    private readonly backend: UiFontBackend | null = browserUiFontBackend(),
  ) {
    // A face downloads only once text needs it, and fitted text measured before that has to measure again.
    backend?.onLoadingDone(loaded => {
      for (let index = 0; index < loaded.length; index++) {
        if (this.faces.indexOf(loaded[index]) >= 0) {
          this.changed();
          return;
        }
      }
    });
  }

  version(): number {
    return this.changes;
  }

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  apply(family: string, faces: readonly SystemFontFace[], fileUrl: (faceId: string) => string): void {
    const own = faces.filter(face => face.family === family && face.remoteRenderable);
    const key = `${family}|${own.map(face => face.faceId).join(',')}`;
    if (key === this.key) return;
    this.key = key;

    const backend = this.backend;
    if (backend !== null) {
      for (let index = 0; index < this.faces.length; index++) backend.remove(this.faces[index]);
    }
    this.faces = [];

    const stack = family ? `${quoteFamily(family)}, ${UI_FONT_FAMILY}` : '';
    if (family && backend !== null) {
      for (let index = 0; index < own.length; index++) {
        const face = own[index];
        const created = backend.create(UI_FONT_FAMILY, `url(${fileUrl(face.faceId)})`, {
          weight: String(face.weight),
          style: face.slant === 'upright' ? 'normal' : face.slant,
        });
        backend.add(created);
        this.faces.push(created);
      }
    }

    if (supportsCustomProperties()) {
      if (stack) this.root.style.setProperty('--font-sans', `${stack}, var(--font-sans-system)`);
      else this.root.style.removeProperty('--font-sans');
    } else {
      // The compatibility floor ships every token resolved, so only an inline family can still move it.
      const value = stack ? `${stack}, sans-serif` : '';
      this.root.style.fontFamily = value;
      const body = this.root.ownerDocument.body;
      if (body) body.style.fontFamily = value;
    }

    this.changed();
  }

  private changed(): void {
    this.changes++;
    for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
  }
}

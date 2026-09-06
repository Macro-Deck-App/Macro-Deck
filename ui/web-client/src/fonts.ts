export type FontFaceStatus = 'loading' | 'ready' | 'failed';

export function internalFontFamily(faceId: string): string {
  return `MacroDeckFont_${faceId}`;
}

function descriptorsFor(faceId: string): FontFaceDescriptors {
  // The trailing `-2`, `-3`... is the catalog's collision suffix for faces sharing all four id
  // components, so it has to be tolerated here or those faces get no descriptors at all.
  const match = /-(\d+)-\d+-(upright|italic|oblique)(?:-\d+)?$/.exec(faceId);
  if (match === null) return {};
  return { weight: match[1], style: match[2] === 'upright' ? 'normal' : match[2] };
}

export interface FontFaceBackend {
  create(family: string, source: string, descriptors: FontFaceDescriptors): { load(): Promise<unknown> };
  add(face: unknown): void;
}

export function browserFontFaceBackend(): FontFaceBackend | null {
  if (typeof document === 'undefined' || typeof FontFace === 'undefined' || !('fonts' in document)) {
    return null;
  }
  return {
    create: (family, source, descriptors) => new FontFace(family, source, descriptors),
    add: face => (document.fonts as unknown as { add(face: unknown): void }).add(face),
  };
}

export class FontLoader {
  private readonly statuses: { [faceId: string]: FontFaceStatus } = {};
  private readonly listeners: Array<() => void> = [];

  constructor(
    private readonly baseUrl: string,
    private readonly backend: FontFaceBackend | null = browserFontFaceBackend(),
  ) {}

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  ready(faceId: string): boolean {
    if (!faceId) return true;
    const status = this.statuses[faceId];
    if (status === undefined) {
      this.load(faceId);
      return false;
    }
    return status !== 'loading';
  }

  private load(faceId: string): void {
    const backend = this.backend;
    if (backend === null) {
      // No FontFace API: the face reports as settled so text is never held back forever on an engine
      // that could not have loaded it anyway.
      this.statuses[faceId] = 'failed';
      return;
    }

    this.statuses[faceId] = 'loading';
    const url = `${this.baseUrl}/api/system/fonts/${encodeURIComponent(faceId)}/file`;
    const face = backend.create(internalFontFamily(faceId), `url(${url})`, descriptorsFor(faceId));

    face.load().then(
      loaded => {
        backend.add(loaded);
        this.settle(faceId, 'ready');
      },
      () => this.settle(faceId, 'failed'));
  }

  private settle(faceId: string, status: FontFaceStatus): void {
    this.statuses[faceId] = status;
    for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
  }
}

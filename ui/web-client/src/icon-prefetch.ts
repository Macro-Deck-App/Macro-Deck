import {
  collectIconPrefetchTargets,
  type Folder,
  type IconPrefetchTarget,
} from '@macro-deck/runtime';

// Warmed one at a time, at idle: this exists to make a folder switch instant, not to race it.
const RETRY_LIMIT = 3;
const RETRY_DELAY_MS = 1000;

export interface IconPrefetchOptions {
  baseUrl(): string;
  folders(): readonly Folder[];
  currentFolderId(): string | null;
  resolveGrid(folder: Folder): { cols: number; rows: number; spacing: number };
  outerMargin: number;
  load?(url: string): Promise<void>;
  schedule?(work: () => void): void;
}

function browserLoader(url: string): Promise<void> {
  return new Promise<void>(resolve => {
    const image = new Image();
    image.onload = () => resolve();
    // A rendition that will not load is not worth retrying here: the widget itself will ask for it
    // again when it is actually drawn, and that request carries the real error handling.
    image.onerror = () => resolve();
    image.src = url;
  });
}

function idle(work: () => void): void {
  const requestIdle = (window as unknown as {
    requestIdleCallback?: (callback: () => void) => number;
  }).requestIdleCallback;

  if (typeof requestIdle === 'function') requestIdle(work);
  else setTimeout(work, 0);
}

export class IconPrefetch {
  private readonly done: { [key: string]: true } = {};
  private running = false;
  private retries = 0;

  constructor(private readonly options: IconPrefetchOptions) {}

  warm(): void {
    if (this.running) return;
    this.running = true;
    const schedule = this.options.schedule || idle;
    schedule(() => this.run());
  }

  private run(): void {
    this.running = false;

    const targets = this.targets();
    if (targets === null) {
      // No viewport yet, so no cell size, so no rendition to ask for. Try again shortly rather than
      // guessing a size and warming the wrong one.
      if (this.retries < RETRY_LIMIT) {
        this.retries++;
        setTimeout(() => this.warm(), RETRY_DELAY_MS);
      }
      return;
    }
    this.retries = 0;

    const load = this.options.load || browserLoader;
    for (let index = 0; index < targets.length; index++) {
      const target = targets[index];
      const key = `${target.iconId}@${target.size}`;
      if (this.done[key]) continue;
      this.done[key] = true;
      void load(this.urlFor(target));
    }
  }

  private urlFor(target: IconPrefetchTarget): string {
    return `${this.options.baseUrl()}/api/icons/${encodeURIComponent(target.iconId)}/image?size=${target.size}`;
  }

  private targets(): IconPrefetchTarget[] | null {
    if (window.innerWidth <= 0 || window.innerHeight <= 0) return null;

    return collectIconPrefetchTargets(this.options.folders(), {
      excludeFolderId: this.options.currentFolderId() || undefined,
      viewportWidth: window.innerWidth,
      viewportHeight: window.innerHeight,
      outerMargin: this.options.outerMargin,
      devicePixelRatio: window.devicePixelRatio || 1,
      resolveGrid: folder => this.options.resolveGrid(folder),
    });
  }
}

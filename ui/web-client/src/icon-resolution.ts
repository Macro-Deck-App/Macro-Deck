import { iconSizeBucket, isIconUiResource, type UiResource, type UiResourceHint } from '@macro-deck/runtime';

export type IconResolution = 'auto' | 128 | 256 | 512;

export const ICON_RESOLUTIONS: readonly IconResolution[] = ['auto', 128, 256, 512];

export const DEFAULT_ICON_RESOLUTION: IconResolution = 'auto';

const STORAGE_KEY = 'macro-deck.icon-resolution';

export interface IconSizeRange {
  min: number;
  max: number;
}

export class IconResolutionStore {
  private resolution: IconResolution;
  private observed: { [key: string]: number } = {};
  private readonly listeners: Array<() => void> = [];
  private readonly observedListeners: Array<() => void> = [];

  constructor() {
    this.resolution = IconResolutionStore.readPreference();
  }

  get(): IconResolution {
    return this.resolution;
  }

  set(resolution: IconResolution): void {
    if (resolution === this.resolution) return;
    this.resolution = resolution;
    this.observed = {};

    try {
      window.localStorage.setItem(STORAGE_KEY, String(resolution));
    } catch {
      // A device with storage disabled still renders; it just forgets the choice on reload.
    }

    notify(this.listeners);
    notify(this.observedListeners);
  }

  onChange(listener: () => void): () => void {
    return subscribe(this.listeners, listener);
  }

  onObservedChange(listener: () => void): () => void {
    return subscribe(this.observedListeners, listener);
  }

  sizeFor(resource: UiResource | undefined, hint?: UiResourceHint): number | undefined {
    if (!isIconUiResource(resource)) return undefined;
    if (this.resolution !== 'auto') return this.resolution;

    const displayPx = hint?.displayPx;
    if (displayPx === undefined || !(displayPx > 0)) return undefined;

    // Never smaller than what this icon already got: a resize or rotation that crosses a bucket boundary
    // would otherwise swap every URL and refetch, and flap when it sits right on the boundary.
    const key = `${hint?.widgetId ?? ''}|${resource!.resourceId}`;
    const previous = this.observed[key] ?? 0;
    const size = Math.max(previous, iconSizeBucket(displayPx));
    if (size !== previous) {
      this.observed[key] = size;
      notify(this.observedListeners);
    }
    return size;
  }

  observedRange(widgetIds: readonly string[]): IconSizeRange | null {
    let min = Infinity;
    let max = -Infinity;
    for (const key in this.observed) {
      if (widgetIds.indexOf(key.slice(0, key.indexOf('|'))) < 0) continue;
      min = Math.min(min, this.observed[key]);
      max = Math.max(max, this.observed[key]);
    }
    return max > 0 ? { min, max } : null;
  }

  private static readPreference(): IconResolution {
    try {
      const stored = window.localStorage.getItem(STORAGE_KEY);
      for (let index = 0; index < ICON_RESOLUTIONS.length; index++) {
        if (String(ICON_RESOLUTIONS[index]) === stored) return ICON_RESOLUTIONS[index];
      }
      return DEFAULT_ICON_RESOLUTION;
    } catch {
      return DEFAULT_ICON_RESOLUTION;
    }
  }
}

function notify(listeners: ReadonlyArray<() => void>): void {
  for (let index = 0; index < listeners.length; index++) listeners[index]();
}

function subscribe(listeners: Array<() => void>, listener: () => void): () => void {
  listeners.push(listener);
  return () => {
    const at = listeners.indexOf(listener);
    if (at >= 0) listeners.splice(at, 1);
  };
}

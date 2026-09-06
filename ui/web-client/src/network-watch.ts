export interface NetworkWatchTarget {
  retryNow(): void;
}

export interface NetworkWatchOptions {
  win?: Window;
}

export function startNetworkWatch(target: NetworkWatchTarget, options?: NetworkWatchOptions): () => void {
  const settings = options === undefined ? {} : options;
  const win = settings.win === undefined ? window : settings.win;

  const listener = (): void => target.retryNow();
  win.addEventListener('online', listener);

  return () => win.removeEventListener('online', listener);
}

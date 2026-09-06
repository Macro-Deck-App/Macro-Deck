// An icon's URL is stable for the life of its bytes, so a repaint never re-issues a request that
// failed: without a reload of its own the tile stays blank until the whole page is loaded again.
const RETRY_DELAYS_MS = [1000, 3000, 10000];

interface ImageRecovery {
  attempt: number;
  timer: ReturnType<typeof setTimeout> | null;
  failed: boolean;
}

interface RecoverableImage extends HTMLImageElement {
  __mdImageRecovery?: ImageRecovery;
}

function reload(image: HTMLImageElement): void {
  const src = image.getAttribute('src');
  if (src === null || src === '') return;
  // A tile dropped between the failure and the retry is nobody's icon any more.
  if (!image.ownerDocument.contains(image)) return;
  // Assigning the same value is not a new load in any engine, so the attribute goes away first.
  image.removeAttribute('src');
  image.setAttribute('src', src);
}

function cancel(state: ImageRecovery): void {
  if (state.timer === null) return;
  clearTimeout(state.timer);
  state.timer = null;
}

function scheduleRetry(image: HTMLImageElement, state: ImageRecovery): void {
  if (state.timer !== null) return;
  const delay = RETRY_DELAYS_MS[state.attempt];
  if (delay === undefined) return;
  state.attempt++;
  state.timer = setTimeout(() => {
    state.timer = null;
    reload(image);
  }, delay);
}

function recoveryOf(image: HTMLImageElement): ImageRecovery {
  const carrier = image as RecoverableImage;
  const existing = carrier.__mdImageRecovery;
  if (existing !== undefined) return existing;

  const state: ImageRecovery = { attempt: 0, timer: null, failed: false };
  carrier.__mdImageRecovery = state;
  image.addEventListener('load', () => {
    state.failed = false;
    state.attempt = 0;
  });
  image.addEventListener('error', () => {
    state.failed = true;
    scheduleRetry(image, state);
  });
  return state;
}

export function setImageSource(image: HTMLImageElement, src: string): void {
  const state = recoveryOf(image);
  // Assigning the same source again would restart a decode of what is already on screen.
  if (image.getAttribute('src') === src) return;
  cancel(state);
  state.attempt = 0;
  state.failed = false;
  image.setAttribute('src', src);
}

export function reloadFailedImages(root: ParentNode): number {
  const images = root.querySelectorAll('img');
  let reloaded = 0;
  for (let index = 0; index < images.length; index++) {
    const state = (images[index] as RecoverableImage).__mdImageRecovery;
    if (state === undefined || !state.failed) continue;
    cancel(state);
    state.attempt = 0;
    reload(images[index]);
    reloaded++;
  }
  return reloaded;
}

export const CROSSFADE_MS = 220;
export const CROSSFADE_PROMOTE_MS = CROSSFADE_MS + 60;
export const DECODE_GRACE_MS = 100;

export interface ArtworkCrossfadeState<TLook = unknown> {
  settled: string | null;
  settledLook: TLook | null;
  incoming: string | null;
  incomingLook: TLook | null;
  pending: string | null;
  pendingLook: TLook | null;
  preloader: HTMLImageElement | null;
  requestId: number;
  promoteTimer: ReturnType<typeof setTimeout> | null;
  promoted: boolean;
  graceTimer: ReturnType<typeof setTimeout> | null;
}

export function createArtworkCrossfadeState<TLook = unknown>(): ArtworkCrossfadeState<TLook> {
  return {
    settled: null,
    settledLook: null,
    incoming: null,
    incomingLook: null,
    pending: null,
    pendingLook: null,
    preloader: null,
    requestId: 0,
    promoteTimer: null,
    promoted: false,
    graceTimer: null,
  };
}

export function showsBitmap(image: HTMLImageElement | null): boolean {
  return image !== null && image.complete && image.naturalWidth > 0;
}

function finishPreloader(state: ArtworkCrossfadeState): HTMLImageElement | null {
  if (state.graceTimer !== null) {
    clearTimeout(state.graceTimer);
    state.graceTimer = null;
  }
  const preloader = state.preloader;
  if (preloader === null) return null;
  state.preloader = null;
  preloader.onload = null;
  preloader.onerror = null;
  return preloader;
}

function cancelInFlight(state: ArtworkCrossfadeState): void {
  state.requestId++;
  if (state.promoteTimer !== null) {
    clearTimeout(state.promoteTimer);
    state.promoteTimer = null;
  }
  const abandoned = finishPreloader(state);
  // Dropping the source cancels a download nobody is waiting for any more.
  if (abandoned !== null) abandoned.src = '';
  state.incoming = null;
  state.incomingLook = null;
  state.pending = null;
  state.pendingLook = null;
}

function settlePending(state: ArtworkCrossfadeState): void {
  finishPreloader(state);
  state.settled = state.pending;
  state.settledLook = state.pendingLook;
  state.pending = null;
  state.pendingLook = null;
}

export function releaseArtworkCrossfade(state: ArtworkCrossfadeState): void {
  cancelInFlight(state);
  state.settled = null;
  state.settledLook = null;
  state.promoted = false;
}

export function takePromotion(state: ArtworkCrossfadeState): boolean {
  const promoted = state.promoted;
  state.promoted = false;
  return promoted;
}

export function swapArtwork<TLook>(
  state: ArtworkCrossfadeState<TLook>,
  next: string | null,
  look: TLook,
  crossfade: boolean,
  outgoingShown: boolean,
  repaint: () => void,
): void {
  if (crossfade) {
    // A crossfade eases filter and opacity across the change, so its layers follow the current look.
    // Zoom, offset and fit snap with it, which holds only while crossfade producers keep them fixed.
    state.settledLook = look;
    if (state.incoming !== null) state.incomingLook = look;
    if (state.pending !== null) state.pendingLook = look;
  }

  if (next === state.settled) {
    if (state.pending !== null || (state.incoming !== null && state.incoming !== state.settled)) {
      cancelInFlight(state);
    }
    state.settledLook = look;
    return;
  }
  if (next !== null && next === state.pending) {
    state.pendingLook = look;
    return;
  }
  if (next !== null && next === state.incoming) {
    state.incomingLook = look;
    return;
  }

  cancelInFlight(state);

  if (next === null || state.settled === null || (!crossfade && !outgoingShown)) {
    state.settled = next;
    state.settledLook = look;
    return;
  }

  const request = state.requestId;
  const preloader = new Image();
  state.preloader = preloader;
  state.pending = next;
  state.pendingLook = look;
  const current = () => request === state.requestId && state.preloader === preloader;

  if (crossfade) {
    preloader.onload = () => {
      if (!current()) return;
      finishPreloader(state);
      state.incoming = state.pending;
      state.incomingLook = state.pendingLook;
      state.pending = null;
      state.pendingLook = null;
      repaint();
      state.promoteTimer = setTimeout(() => {
        state.promoteTimer = null;
        if (request !== state.requestId) return;

        // The incoming element itself becomes the settled one: giving its source to the other element would
        // start an animated picture over there, a jump back to its first frame once the overlay goes.
        state.settled = state.incoming;
        state.settledLook = state.incomingLook;
        state.incoming = null;
        state.incomingLook = null;
        state.promoted = true;
        repaint();
      }, CROSSFADE_PROMOTE_MS);
    };

    // Artwork that cannot be decoded replaces the outgoing one at once, with no fade: holding the previous
    // one would attribute it to whatever the element now stands for.
    preloader.onerror = () => {
      if (!current()) return;
      settlePending(state);
      repaint();
    };

    preloader.src = next;
    return;
  }

  const settle = () => {
    if (!current()) return;
    settlePending(state);
    repaint();
  };
  preloader.onerror = settle;
  preloader.onload = () => {
    if (!current()) return;
    // A hidden page queues decode() until its next commit, which may never come while it stays hidden.
    if (typeof preloader.decode !== 'function' || document.visibilityState !== 'visible') {
      settle();
      return;
    }
    state.graceTimer = setTimeout(settle, DECODE_GRACE_MS);
  };
  preloader.src = next;

  if (showsBitmap(preloader)) {
    settlePending(state);
    return;
  }
  if (typeof preloader.decode === 'function') {
    preloader.decode().then(settle, () => {
      if (!showsBitmap(preloader)) settle();
    });
  }
}

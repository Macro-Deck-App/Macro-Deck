export const CROSSFADE_MS = 220;
export const CROSSFADE_PROMOTE_MS = CROSSFADE_MS + 60;

export interface ArtworkCrossfadeState {
  settled: string | null;
  incoming: string | null;
  requestId: number;
  promoteTimer: ReturnType<typeof setTimeout> | null;
  promoteFrame: number | null;
}

export function createArtworkCrossfadeState(): ArtworkCrossfadeState {
  return { settled: null, incoming: null, requestId: 0, promoteTimer: null, promoteFrame: null };
}

function clearPromoteFrame(state: ArtworkCrossfadeState): void {
  if (state.promoteFrame !== null) {
    cancelAnimationFrame(state.promoteFrame);
    state.promoteFrame = null;
  }
}

export function releaseArtworkCrossfade(state: ArtworkCrossfadeState): void {
  state.requestId++;
  if (state.promoteTimer !== null) {
    clearTimeout(state.promoteTimer);
    state.promoteTimer = null;
  }
  clearPromoteFrame(state);
  state.settled = null;
  state.incoming = null;
}

export function swapArtwork(
  state: ArtworkCrossfadeState,
  next: string | null,
  crossfade: boolean,
  repaint: () => void,
): void {
  if (next === state.settled) return;

  state.requestId++;
  if (state.promoteTimer !== null) {
    clearTimeout(state.promoteTimer);
    state.promoteTimer = null;
  }
  clearPromoteFrame(state);
  state.incoming = null;

  if (!crossfade || next === null || state.settled === null) {
    state.settled = next;
    return;
  }

  const request = state.requestId;
  const image = new Image();

  image.onload = () => {
    if (request !== state.requestId) return;
    state.incoming = next;
    repaint();
    state.promoteTimer = setTimeout(() => {
      state.promoteTimer = null;
      if (request !== state.requestId) return;

      // The settled layer takes the new source first and the overlay is dropped only once that layer
      // has actually painted it. Doing both in one tick lets the browser blank the settled <img> for a
      // frame while it re-resolves the (already cached) source - one dropped frame at the end of an
      // otherwise smooth fade, which is exactly what reads as a stutter.
      state.settled = next;
      repaint();
      state.promoteFrame = requestAnimationFrame(() => {
        state.promoteFrame = requestAnimationFrame(() => {
          state.promoteFrame = null;
          if (request !== state.requestId) return;
          state.incoming = null;
          repaint();
        });
      });
    }, CROSSFADE_PROMOTE_MS);
  };

  // Artwork that cannot be decoded replaces the outgoing one at once, with no fade: holding the previous
  // one would attribute it to whatever the element now stands for.
  image.onerror = () => {
    if (request !== state.requestId) return;
    state.settled = next;
    repaint();
  };

  image.src = next;
}

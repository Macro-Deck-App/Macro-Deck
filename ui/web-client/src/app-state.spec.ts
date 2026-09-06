import { AppState, INITIAL_CONDITIONS, screenFor } from './app-state';

const conditions = (overrides: Partial<typeof INITIAL_CONDITIONS> = {}) =>
  ({ ...INITIAL_CONDITIONS, probed: true, ...overrides });

describe('which screen the client shows', () => {
  it('shows nothing decisive until the host has been probed once', () => {
    expect(screenFor(INITIAL_CONDITIONS)).toBe('starting');
  });

  it('shows the locked key ring above everything else', () => {
    // Nothing the host holds can be read while it is locked, so any other screen would be a lie.
    expect(screenFor(conditions({
      keyRingLocked: true, setupRequired: true, authenticated: true, connected: true,
    }))).toBe('keyRingLocked');
  });

  it('asks for first-time setup before asking anyone to sign in', () => {
    // There is no account to sign in to yet.
    expect(screenFor(conditions({ setupRequired: true }))).toBe('setupRequired');
  });

  it('asks for a sign-in once setup is done', () => {
    expect(screenFor(conditions())).toBe('signedOut');
  });

  it('waits for the connection once signed in', () => {
    expect(screenFor(conditions({ authenticated: true }))).toBe('connecting');
  });

  it('shows the deck once connected', () => {
    expect(screenFor(conditions({ authenticated: true, connected: true }))).toBe('deck');
  });

  it('keeps a deck that has already painted when the connection drops', () => {
    // The widgets on screen are still the last thing the host said. Replacing a working deck with a
    // spinner every time the network hiccups is worse than showing slightly stale buttons.
    expect(screenFor(conditions({
      authenticated: true, connected: false, deckRendered: true,
    }))).toBe('deck');
  });

  it('does not keep a deck for someone who has been signed out', () => {
    expect(screenFor(conditions({
      authenticated: false, connected: true, deckRendered: true,
    }))).toBe('signedOut');
  });

  it('does not keep a deck behind a key ring that has since locked', () => {
    expect(screenFor(conditions({
      keyRingLocked: true, authenticated: true, deckRendered: true,
    }))).toBe('keyRingLocked');
  });
});

describe('AppState', () => {
  it('starts before the first probe', () => {
    expect(new AppState().screen.get()).toBe('starting');
  });

  it('tells subscribers when the screen changes', () => {
    const state = new AppState();
    const seen: string[] = [];
    state.screen.subscribe((screen: string) => seen.push(screen));

    state.set({ probed: true });
    state.set({ authenticated: true });
    state.set({ connected: true });

    expect(seen).toEqual(['signedOut', 'connecting', 'deck']);
  });

  it('says nothing when a change leaves the screen where it was', () => {
    const state = new AppState();
    state.set({ probed: true, authenticated: true, connected: true });
    const seen: string[] = [];
    state.screen.subscribe((screen: string) => seen.push(screen));

    // Still the deck, so nothing to repaint.
    state.set({ deckRendered: true });

    expect(seen).toEqual([]);
  });

  it('ignores a change that sets what is already set', () => {
    const state = new AppState();
    state.set({ probed: true });
    const seen: unknown[] = [];
    state.conditions.subscribe((value: unknown) => seen.push(value));

    state.set({ probed: true });

    expect(seen).toEqual([]);
  });
});

import { ActionButtonData, ButtonStateDefinition } from './widget.interface';
import {
  DEFAULT_OFF_STATE_ID,
  DEFAULT_ON_STATE_ID,
  adoptProvidedStates,
  defaultStateDefinitions,
  resolveActiveStateId,
} from './button-state.util';

describe('defaultStateDefinitions', () => {
  it('yields an Off/On pair with distinct, reserved ids', () => {
    const states = defaultStateDefinitions();

    expect(states.map(s => s.id)).toEqual([DEFAULT_OFF_STATE_ID, DEFAULT_ON_STATE_ID]);
    expect(states.map(s => s.label)).toEqual(['Off', 'On']);
    expect(new Set(states.map(s => s.id)).size).toBe(2);
  });

  it('never carries a single-state background into the states it seeds', () => {
    // Turning State Mode on used to copy the single-state colour into `off` while leaving it as the
    // root fallback too, so a colour the user had moved away from came back on the next save. `on`
    // keeps a default of its own, which was never carried over from the button.
    const states = defaultStateDefinitions({ label: 'Play', backgroundColor: '#22c55e' });

    expect(states.map(s => s.appearance?.backgroundColor)).toEqual([undefined, '#ef4444']);
    expect(states.map(s => s.appearance?.label)).toEqual(['Play', 'Play']);
  });
});

describe('adoptProvidedStates', () => {
  it('preserves each state\'s authored appearance by matching id across a label change', () => {
    const current: ButtonStateDefinition[] = [
      { id: 'live', label: 'Live', appearance: { backgroundColor: '#00ff00' } },
      { id: 'offline', label: 'Offline', appearance: { backgroundColor: '#ff0000' } },
    ];

    const adopted = adoptProvidedStates(current, [
      { id: 'live', label: 'Streaming' },
      { id: 'offline', label: 'Not streaming' },
    ]);

    expect(adopted).toEqual([
      { id: 'live', label: 'Streaming', appearance: { backgroundColor: '#00ff00' } },
      { id: 'offline', label: 'Not streaming', appearance: { backgroundColor: '#ff0000' } },
    ]);
  });

  it('starts a newly-provided id from the provider\'s own defaults', () => {
    const adopted = adoptProvidedStates([], [
      {
        id: 'muted',
        label: 'Muted',
        defaultAppearance: { backgroundColor: '#c53030', labelColor: '#ffffff', iconId: 'icon-1' },
      },
    ]);

    expect(adopted).toEqual([
      {
        id: 'muted',
        label: 'Muted',
        appearance: { label: 'Muted', backgroundColor: '#c53030', labelColor: '#ffffff', iconId: 'icon-1' },
      },
    ]);
  });

  it('labels a newly-provided state with its own name when the provider suggests no text', () => {
    const adopted = adoptProvidedStates([], [{ id: 'starting', label: 'Starting' }]);

    expect(adopted).toEqual([{ id: 'starting', label: 'Starting', appearance: { label: 'Starting' } }]);
  });

  it('leaves an already-configured state alone when the provider suggests defaults for it', () => {
    const current: ButtonStateDefinition[] = [
      { id: 'muted', label: 'Muted', appearance: { backgroundColor: '#00ff00' } },
    ];

    const adopted = adoptProvidedStates(current, [
      { id: 'muted', label: 'Muted', defaultAppearance: { backgroundColor: '#c53030', label: 'Mic off' } },
    ]);

    expect(adopted).toEqual([
      { id: 'muted', label: 'Muted', appearance: { backgroundColor: '#00ff00' } },
    ]);
  });

  it('drops an id the provider no longer exposes, so the live set is exactly the provided one', () => {
    const current: ButtonStateDefinition[] = [
      { id: 'a', label: 'A', appearance: { backgroundColor: '#111111' } },
      { id: 'b', label: 'B', appearance: { backgroundColor: '#222222' } },
    ];

    const adopted = adoptProvidedStates(current, [{ id: 'a', label: 'A renamed' }]);

    expect(adopted).toEqual([
      { id: 'a', label: 'A renamed', appearance: { backgroundColor: '#111111' } },
    ]);
  });

  it('does not carry a vanished id\'s appearance onto a newly provided one', () => {
    const current: ButtonStateDefinition[] = [
      { id: 'unavailable', label: 'Unavailable', appearance: { backgroundColor: '#808080' } },
    ];

    const adopted = adoptProvidedStates(current, [{ id: 'starting', label: 'Starting' }]);

    expect(adopted[0].appearance?.backgroundColor).toBeUndefined();
  });
});

describe('resolveActiveStateId', () => {
  function data(states: ButtonStateDefinition[], activeStateId?: string): ActionButtonData {
    return { stateMode: true, states, activeStateId };
  }

  it('prefers the pushed id when it names a real state', () => {
    const d = data([{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }], 'a');

    expect(resolveActiveStateId(d, 'b')).toBe('b');
  });

  it('falls back to the stored activeStateId when the pushed id is unknown', () => {
    const d = data([{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }], 'b');

    expect(resolveActiveStateId(d, 'does-not-exist')).toBe('b');
  });

  it('falls back to the first declared state when neither the pushed nor the stored id resolves', () => {
    const d = data([{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }], 'gone');

    expect(resolveActiveStateId(d, 'also-gone')).toBe('a');
  });

  it('falls back to the first declared state for a provider that has not answered yet (no pushed id at all)', () => {
    const d = data([{ id: 'starting', label: 'Starting' }, { id: 'live', label: 'Live' }]);

    expect(resolveActiveStateId(d, undefined)).toBe('starting');
  });
});

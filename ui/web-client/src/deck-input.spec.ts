import { GridWidget, HardwareInputMap } from '@macro-deck/runtime';
import { DeckInput, DeckInputTarget } from './deck-input';

describe('deck input', () => {
  const map: HardwareInputMap = {
    keys: [
      { key: '1', event: { kind: 'selectIndex', index: 0 } },
      { key: '2', event: { kind: 'selectIndex', index: 1 } },
      { key: 'Enter', event: { kind: 'activate' } },
      { key: 'Escape', event: { kind: 'back' } },
    ],
    wheel: { stepPx: 40 },
  } as HardwareInputMap;

  const widget = (id: string, x: number, y: number): GridWidget =>
    ({ id, folderId: 'f', x, y, w: 1, h: 1, type: 'action-button', data: {} }) as GridWidget;

  let activated: string[];
  let triggered: string[];
  let backs: number;
  let focusEvents: Array<string | null>;
  let modal: boolean;
  let claims: string[];
  let widgets: GridWidget[];
  let target: DeckInputTarget;
  let inputs: DeckInput[];

  beforeEach(() => {
    activated = [];
    triggered = [];
    backs = 0;
    focusEvents = [];
    modal = false;
    claims = [];
    widgets = [];
    inputs = [];
    target = {
      widgets: () => widgets,
      activateWidget: id => {
        activated.push(id);
        return claims.indexOf(id) >= 0;
      },
      triggerWidget: w => { triggered.push(w.id); },
      goBack: () => { backs++; },
      modalOpen: () => modal,
      focusChanged: id => { focusEvents.push(id); },
    };
  });

  afterEach(() => {
    while (inputs.length > 0) (inputs.pop() as DeckInput).dispose();
  });

  const attach = (hardware: HardwareInputMap | undefined) => {
    const input = new DeckInput(target, hardware, document);
    inputs.push(input);
    return input;
  };

  const key = (value: string) => {
    const event = new Event('keydown') as Event & { key: string; repeat: boolean };
    event.key = value;
    event.repeat = false;
    document.dispatchEvent(event);
  };

  it('focuses nothing and listens to nothing on a client with no controls', () => {
    widgets = [widget('a', 0, 0)];
    const input = attach(undefined);

    key('Enter');

    // The default client must be untouched, not merely unaffected: a focus ring on its first widget
    // is something nobody asked for.
    expect(input.focusedWidgetId()).toBeNull();
    expect(activated.length).toBe(0);
  });

  it('walks the deck in the order the eye reads it, not the order the widgets arrived', () => {
    // Deliberately out of visual order: the second row's widget arrives first.
    widgets = [widget('bottom-left', 0, 1), widget('top-right', 1, 0), widget('top-left', 0, 0)];
    const input = attach(map);

    expect(input.focusedWidgetId()).toBe('top-left');

    const wheel = new Event('wheel') as Event & { deltaY: number; deltaX: number };
    wheel.deltaY = 40;
    wheel.deltaX = 0;
    document.dispatchEvent(wheel);

    expect(input.focusedWidgetId()).toBe('top-right');
  });

  it('wraps at both ends, because an encoder has no travel limit', () => {
    widgets = [widget('a', 0, 0), widget('b', 1, 0)];
    const input = attach(map);

    const wheel = (deltaY: number) => {
      const event = new Event('wheel') as Event & { deltaY: number; deltaX: number };
      event.deltaY = deltaY;
      event.deltaX = 0;
      document.dispatchEvent(event);
    };

    wheel(-40);
    expect(input.focusedWidgetId()).toBe('b');

    wheel(40);
    expect(input.focusedWidgetId()).toBe('a');
  });

  it('lets a widget that claims the press run its own lifecycle', () => {
    widgets = [widget('a', 0, 0)];
    claims = ['a'];
    attach(map);

    key('Enter');

    // Firing the tile trigger as well is how one physical press runs two flows.
    expect(activated).toEqual(['a']);
    expect(triggered).toEqual([]);
  });

  it('falls back to the tile trigger for a widget that claims nothing', () => {
    widgets = [widget('a', 0, 0)];
    attach(map);

    key('Enter');

    expect(triggered).toEqual(['a']);
  });

  it('aims and presses in one action for a numbered button', () => {
    widgets = [widget('a', 0, 0), widget('b', 1, 0)];
    const input = attach(map);

    key('2');

    expect(input.focusedWidgetId()).toBe('b');
    expect(triggered).toEqual(['b']);
  });

  it('leaves the folder on back, unless a dialog is up and owns the gesture', () => {
    widgets = [widget('a', 0, 0)];
    attach(map);

    key('Escape');
    expect(backs).toBe(1);

    modal = true;
    key('Escape');

    // Navigating underneath would leave someone staring at a dialog for a folder they just left.
    expect(backs).toBe(1);
  });

  it('puts the cursor back at the start when the deck is replaced', () => {
    widgets = [widget('a', 0, 0), widget('b', 1, 0)];
    const input = attach(map);
    key('2');
    expect(input.focusedWidgetId()).toBe('b');

    widgets = [widget('x', 0, 0), widget('y', 1, 0)];
    input.resetFocus();

    // Carrying the position across a folder switch lands the cursor on an unrelated tile.
    expect(input.focusedWidgetId()).toBe('x');
  });

  it('reports every move, so the ring can follow', () => {
    widgets = [widget('a', 0, 0), widget('b', 1, 0)];
    attach(map);

    key('2');

    expect(focusEvents[focusEvents.length - 1]).toBe('b');
  });

  it('focuses nothing in an empty folder rather than the tile that used to be there', () => {
    widgets = [widget('a', 0, 0)];
    const input = attach(map);
    widgets = [];

    expect(input.focusedWidgetId()).toBeNull();
  });
});

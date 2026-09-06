import { ClientInputEvent } from './client-input-event';
import { HardwareInputMap } from './hardware-input-map';
import { keyboardInputSource, wheelInputSource } from './hardware-input.source';

describe('hardware input sources', () => {
  function harness(map: HardwareInputMap) {
    const target = new EventTarget();
    const events: ClientInputEvent[] = [];
    return { target, events, map, emit: (event: ClientInputEvent) => events.push(event) };
  }

  describe('keyboardInputSource', () => {
    const MAP: HardwareInputMap = {
      keys: [
        { key: '1', event: { kind: 'selectIndex', index: 0 } },
        { key: 'm', event: { kind: 'back' } },
        { key: 'Enter', event: { kind: 'activate' } },
      ],
    };

    it('emits the meaning a target bound to a key', () => {
      const h = harness(MAP);
      keyboardInputSource(h);

      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: '1' }));

      expect(h.events).toEqual([{ kind: 'selectIndex', index: 0 }]);
    });

    it('emits nothing for a key the target did not bind', () => {
      const h = harness(MAP);
      keyboardInputSource(h);

      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: 'q' }));
      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: 'F5' }));

      expect(h.events).toEqual([]);
    });

    it('matches a device that reports a bound letter in upper case', () => {
      const h = harness(MAP);
      keyboardInputSource(h);

      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: 'M' }));

      expect(h.events).toEqual([{ kind: 'back' }]);
    });

    it('treats auto-repeat as one press, not a stream of them', () => {
      const h = harness(MAP);
      keyboardInputSource(h);

      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', repeat: true }));
      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', repeat: true }));

      expect(h.events).toEqual([{ kind: 'activate' }]);
    });

    it('leaves a keystroke alone while the user is typing', () => {
      // The login form lives on the same screen; a target that maps the digits would otherwise eat
      // half of a password.
      const h = harness(MAP);
      const input = document.createElement('input');
      document.body.appendChild(input);
      keyboardInputSource({ ...h, target: input });

      input.dispatchEvent(new KeyboardEvent('keydown', { key: '1', bubbles: true }));
      document.body.removeChild(input);

      expect(h.events).toEqual([]);
    });

    it('stops emitting once torn down', () => {
      const h = harness(MAP);
      const teardown = keyboardInputSource(h);

      teardown();
      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: '1' }));

      expect(h.events).toEqual([]);
    });

    it('attaches nothing for a target with no keys', () => {
      const h = harness({ keys: [] });
      keyboardInputSource(h);

      h.target.dispatchEvent(new KeyboardEvent('keydown', { key: '1' }));

      expect(h.events).toEqual([]);
    });
  });

  describe('wheelInputSource', () => {
    const MAP: HardwareInputMap = { keys: [], wheel: { stepPx: 40 } };

    it('turns accumulated rotation into whole focus steps', () => {
      const h = harness(MAP);
      wheelInputSource(h);

      h.target.dispatchEvent(new WheelEvent('wheel', { deltaY: 80 }));

      expect(h.events).toEqual([{ kind: 'focusMove', delta: 2 }]);
    });

    it('holds rotation below one step rather than dropping it', () => {
      // A free-spinning encoder reports far less than a mouse notch per detent, so discarding the
      // remainder would make slow turns do nothing at all.
      const h = harness(MAP);
      wheelInputSource(h);

      h.target.dispatchEvent(new WheelEvent('wheel', { deltaY: 25 }));
      expect(h.events).toEqual([]);

      h.target.dispatchEvent(new WheelEvent('wheel', { deltaY: 25 }));
      expect(h.events).toEqual([{ kind: 'focusMove', delta: 1 }]);
    });

    it('moves backwards for the opposite rotation', () => {
      const h = harness(MAP);
      wheelInputSource(h);

      h.target.dispatchEvent(new WheelEvent('wheel', { deltaY: -40 }));

      expect(h.events).toEqual([{ kind: 'focusMove', delta: -1 }]);
    });

    it('honours a target that reports its rotation inverted', () => {
      const h = harness({ keys: [], wheel: { stepPx: 40, invert: true } });
      wheelInputSource(h);

      h.target.dispatchEvent(new WheelEvent('wheel', { deltaY: 40 }));

      expect(h.events).toEqual([{ kind: 'focusMove', delta: -1 }]);
    });

    it('attaches nothing for a target with no wheel', () => {
      const h = harness({ keys: [] });
      wheelInputSource(h);

      h.target.dispatchEvent(new WheelEvent('wheel', { deltaY: 400 }));

      expect(h.events).toEqual([]);
    });
  });
});

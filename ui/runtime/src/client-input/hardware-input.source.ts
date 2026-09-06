import { ClientInputEvent } from './client-input-event';
import { HardwareInputMap } from './hardware-input-map';

export interface HardwareInputContext {
  readonly map: HardwareInputMap;
  readonly target: EventTarget;
  readonly emit: (event: ClientInputEvent) => void;
}

export type HardwareInputSource = (context: HardwareInputContext) => () => void;

const NOOP = (): void => { };

export const keyboardInputSource: HardwareInputSource = ({ map, target, emit }) => {
  if (map.keys.length === 0) return NOOP;

  const bindings = new Map(map.keys.map(binding => [binding.key, binding.event]));

  const listener = (event: Event): void => {
    const keyboardEvent = event as KeyboardEvent;
    // A held button must not machine-gun the deck; auto-repeat is not a second press.
    if (keyboardEvent.repeat) return;
    // Never steal a keystroke someone is typing - the login form lives on the same screen, and a
    // target that maps the digits would otherwise eat half of a password.
    if (isEditable(keyboardEvent.target)) return;

    const bound = bindings.get(keyboardEvent.key) ?? bindings.get(keyboardEvent.key.toLowerCase());
    if (!bound) return;

    keyboardEvent.preventDefault();
    emit(bound);
  };

  target.addEventListener('keydown', listener);
  return () => target.removeEventListener('keydown', listener);
};

export const wheelInputSource: HardwareInputSource = ({ map, target, emit }) => {
  const wheel = map.wheel;
  if (!wheel || wheel.stepPx <= 0) return NOOP;

  let accumulated = 0;

  const listener = (event: Event): void => {
    accumulated += (event as WheelEvent).deltaY;

    const steps = Math.trunc(accumulated / wheel.stepPx);
    if (steps === 0) return;

    accumulated -= steps * wheel.stepPx;
    emit({ kind: 'focusMove', delta: wheel.invert ? -steps : steps });
  };

  target.addEventListener('wheel', listener, { passive: true });
  return () => target.removeEventListener('wheel', listener);
};

function isEditable(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  if (!element || typeof element.tagName !== 'string') return false;

  const tag = element.tagName.toLowerCase();
  return tag === 'input' || tag === 'textarea' || tag === 'select' || element.isContentEditable === true;
}

import { ClientInputEvent } from './client-input-event';

export interface KeyBinding {
  readonly key: string;
  readonly event: ClientInputEvent;
}

export interface WheelBinding {
  readonly stepPx: number;
  readonly invert?: boolean;
}

export interface HardwareInputMap {
  readonly keys: readonly KeyBinding[];
  readonly wheel?: WheelBinding;
}

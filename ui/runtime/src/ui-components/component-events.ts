export const UiComponentEvents = {
  Change: 'change',

  Adjust: 'adjust',

  Press: 'press',

  LongPress: 'long-press',

  PressStart: 'press-start',

  PressEnd: 'press-end',

  Reveal: 'reveal',

  DoublePress: 'double-press',

  Drag: 'drag',

  DragEnd: 'drag-end',

  Swipe: 'swipe',

  Pinch: 'pinch',

  PinchEnd: 'pinch-end',
} as const;

export const UI_COMPONENT_EVENTS_WELL_KNOWN: readonly string[] = Object.values(UiComponentEvents);

export const UiComponentEvents = {
  Change: 'change',

  Adjust: 'adjust',

  Press: 'press',

  LongPress: 'long-press',

  PressStart: 'press-start',

  PressEnd: 'press-end',

  Reveal: 'reveal',
} as const;

export const UI_COMPONENT_EVENTS_WELL_KNOWN: readonly string[] = Object.values(UiComponentEvents);
